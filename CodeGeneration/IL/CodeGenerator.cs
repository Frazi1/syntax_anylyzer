﻿using MathLang.Tree.Nodes.Declarations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using MathLang.Extensions;
using MathLang.Tree.Nodes.Enums;
using MathLang.Tree.Nodes.Expressions;
using MathLang.Tree.Nodes.Interfaces;
using MathLang.Tree.Nodes.Statements;
using MathLang.Tree.Semantics;

namespace MathLang.CodeGeneration
{
    public class CodeGenerator
    {
        protected AssemblyBuilder assemblyBuilder;

        protected AssemblyName assemblyName;

        protected ModuleBuilder module;

        protected MethodBuilder mainMethodbuilder;

        protected Dictionary<string, TypeBuilder> classTypeBuilders = new Dictionary<string, TypeBuilder>();

        protected Dictionary<string, FieldBuilder> fieldBuilders = new Dictionary<string, FieldBuilder>();

        /// <summary>
        /// contains both method builders (for our methods) and method infos (for external libraries methods)
        /// </summary>
        protected Dictionary<string, MethodInfo> funcsMethodBuilders = new Dictionary<string, MethodInfo>();

        protected Dictionary<string, LocalBuilder> varsLocalBuilders = new Dictionary<string, LocalBuilder>();


        public CodeGenerator(string assName, Tree.Nodes.Program programNode)
        {
            // create a dynamic assembly and module 
            assemblyName = new AssemblyName();
            assemblyName.Name = assName;

            assemblyBuilder = Thread.GetDomain()
                .DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave);

            module = assemblyBuilder.DefineDynamicModule(assName + ".exe", true);

            GenerateProgram(programNode);

            assemblyBuilder.SetEntryPoint(mainMethodbuilder, PEFileKinds.ConsoleApplication);

            assemblyBuilder.Save(assName + ".exe");
        }

        public void GenerateProgram(Tree.Nodes.Program programNode)
        {
            //create all class and method builders
            foreach (var classNode in programNode.ClassNodes)
            {
                DeclareClass(classNode, module);
            }
            //generate code for all declared classes 
            foreach (var classNode in programNode.ClassNodes)
            {
                GenerateClass(classNode, module);
            }

        }

        public void DeclareClass(ClassDeclaration classNode, ModuleBuilder module)
        {
            if (classNode.IsAttribute) return;
            
            TypeBuilder typeBuilder = null;

            if (!classNode.IsExtern)
            {
                // create public static class
                typeBuilder = module.DefineType(classNode.Name, TypeAttributes.Public |
                                                                TypeAttributes.Class | TypeAttributes.Sealed |
                                                                TypeAttributes.Abstract);

                classTypeBuilders.Add(classNode.Name, typeBuilder);


                if (classNode.VarDeclarationNodes.Count > 0)
                {
                    ConstructorBuilder constructor = typeBuilder.DefineConstructor(
                        MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig |
                        MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, CallingConventions.Standard,
                        new Type[0]);

                    ILGenerator ilGenerator = constructor.GetILGenerator();

                    foreach (var declarationNode in classNode.VarDeclarationNodes)
                    {
                        DeclareField(declarationNode, typeBuilder, ilGenerator);
                    }

                    ilGenerator.Emit(OpCodes.Ret);
                }
            }
            foreach (var funcNode in classNode.FunctionDeclarationNodes)
            {
                DeclareFunc(funcNode, typeBuilder);
            }

        }

        public void DeclareField(VariableDeclaration varDeclarationNode, TypeBuilder typeBuilder, ILGenerator ilGenerator)
        {
            FieldBuilder fieldBuilder = typeBuilder.DefineField(varDeclarationNode.Name,
                varDeclarationNode.ReturnType.ConvertToType(), FieldAttributes.Public | FieldAttributes.Static);

            fieldBuilders.Add(varDeclarationNode.FullName, fieldBuilder);

            if (varDeclarationNode.Value != null)
            {
                GenerateExpression(varDeclarationNode.Value, ilGenerator);
                //pop var from the stack and store in a value
                ilGenerator.Emit(OpCodes.Stsfld, fieldBuilder);
            }
        }

        public void DeclareFunc(FunctionDeclaration functionDeclarationNode, TypeBuilder typeBuilder)
        {
            if (functionDeclarationNode.IsExternal)
            {
                var attribute = functionDeclarationNode.AttributeUsages.FirstOrDefault(att => att.Name.Name == "DotNetRef");

                if (attribute == null) return;

                var paths = attribute.FunctionCallParameters[0].CastTo<StringExpression>().Value.Replace("\"", "").Split('/');

                if (paths.Count() < 3)
                    throw new ArgumentException("No class or method name in attribute");

                string libName = paths[0];
                string className = paths[1];
                string methodName = paths[2];

                var typesNamesTuple2 = GenerationHelper.GetTypesAndNamesOfFuncParams(functionDeclarationNode.ParameterNodes);
                
                var libpath = Path.Combine(CompilerSettings.CurrentDirectory, libName) + ".dll";
                Type externalClassType = Assembly.LoadFrom(libpath).GetTypes().First(t => t.Name == className);

                if (externalClassType == null)
                    throw new ApplicationException($"No external class with name {className} found");

                MethodInfo methodInfo = externalClassType.GetMethod(methodName, typesNamesTuple2.Item1);

                funcsMethodBuilders.Add(functionDeclarationNode.FullName, methodInfo);

                return;
            }



            MethodBuilder methodbuilder;

            //the firs main will be our entry point
            if (functionDeclarationNode.Name == "Main" || mainMethodbuilder == null)
            {
                // set the entry point for the application

                methodbuilder = typeBuilder.DefineMethod("Main", MethodAttributes.HideBySig | MethodAttributes.Static |
                                                                 MethodAttributes.Public, typeof(void), new Type[] { typeof(string[]) });

                mainMethodbuilder = methodbuilder;

            }
            else
            {
                //get tuple of arrays of type and names of func parameters
                var typesNamesTuple = GenerationHelper.GetTypesAndNamesOfFuncParams(functionDeclarationNode.ParameterNodes);

                methodbuilder =
                    typeBuilder.DefineMethod(functionDeclarationNode.Name,
                        MethodAttributes.HideBySig |
                        MethodAttributes.Static |
                        MethodAttributes.Public,
                        functionDeclarationNode.ReturnType.ConvertToType(),
                        typesNamesTuple.Item1);

                //set names to parameters
                for (int i = 0; i < typesNamesTuple.Item2.Length; i++)
                {

                    methodbuilder.DefineParameter(i + 1, ParameterAttributes.None, typesNamesTuple.Item2[i]);
                }
            }

            funcsMethodBuilders.Add(functionDeclarationNode.FullName, methodbuilder);
        }

        public void GenerateClass(ClassDeclaration classNode, ModuleBuilder module)
        {
            //we don't generate external classes, we only declare methods in them 
            //(by finding appropriate methods in external dll)
            if (classNode.IsExtern || classNode.IsAttribute) return;

            TypeBuilder typeBuilder = null;

            if (classTypeBuilders.ContainsKey(classNode.Name))
            {
                typeBuilder = classTypeBuilders[classNode.Name];
            }

            if (typeBuilder == null) return;

            foreach (var funcNode in classNode.FunctionDeclarationNodes)
            {
                GenerateFunc(funcNode, typeBuilder);
            }

            Type helloWorldType = typeBuilder.CreateType();
        }

        public void GenerateFunc(FunctionDeclaration functionDeclarationNode, TypeBuilder typeBuilder)
        {
            if (functionDeclarationNode.IsExternal) return;

            varsLocalBuilders.Clear();

            if (funcsMethodBuilders.Keys.Contains(functionDeclarationNode.FullName))
            {
                var methodbuilder = funcsMethodBuilders[functionDeclarationNode.FullName] as MethodBuilder;

                if (methodbuilder == null) return;

                GenerateFuncStatementBlock(functionDeclarationNode.StatementBlock, methodbuilder);
            }
            else
            {
                throw new NotSupportedException($"Cannot find func {functionDeclarationNode.FullName}");
            }

        }

        public void GenerateFuncStatementBlock(BlockStatement blockStatementNode, MethodBuilder methodBuilder)
        {
            ILGenerator ilGenerator = methodBuilder.GetILGenerator();

            foreach (var statement in blockStatementNode.Statements)
            {
                GenerateStatement(statement, ilGenerator);
            }
            
            if (blockStatementNode.Parent is FunctionDeclaration funcDecl)
            {
                //if(funcDecl.ReturnType == ReturnType.Void)
                //the poblem is that if "ret" is not the last il command, CLR throws InvalidProgramException,
                //so we gotta write this even if function returns not void 
                ilGenerator.Emit(OpCodes.Ret);
            }

        }

        public void GenerateStatement(IStatement statement, ILGenerator ilGenerator)
        {
            switch (statement)
            {
                case VariableAssignment variableAssignment:
                    GenerateVarAssignment(variableAssignment, ilGenerator);
                    break;
                case ArrayElementAssignment arrayElementAssignment:
                    GenerateArrayElementAssignment(arrayElementAssignment, ilGenerator);
                    break;
                case VariableDeclaration declaration:
                    GenerateDeclaration(declaration, ilGenerator);
                    break;
                case FunctionCall functionCall:
                    GenerateFuncCall(functionCall, ilGenerator);
                    break;
                case ReturnStatement returnStatement:
                    GenerateReturnStatement(returnStatement, ilGenerator);
                    break;
                case WhileStatement whileStatement:
                    GenerateWhileStatement(whileStatement, ilGenerator);
                    break;
                case IfStatement ifStatement:
                    GenerateIfStatement(ifStatement, ilGenerator);
                    break;
                case ForStatement forStatement:
                    GenerateForStatement(forStatement, ilGenerator);
                    break;
                case BlockStatement blockStatement:
                    GenerateBlockStatement(blockStatement, ilGenerator);
                    break;
                default:
                    throw new NotImplementedException($"Cannot Generate statement: {statement.GetType()}");
            }
        }

        #region statements
        public void GenerateFuncCall(FunctionCall funcCallNode, ILGenerator ilGenerator)
        {

            foreach (var functionCallParameter in funcCallNode.FunctionCallParameters)
            {
                GenerateExpression(functionCallParameter, ilGenerator);
            }

            if (funcsMethodBuilders.Keys.Contains(funcCallNode.FunctionDeclaration.FullName))
            {
                var methodbuilder = funcsMethodBuilders[funcCallNode.FunctionDeclaration.FullName];

                ilGenerator.EmitCall(OpCodes.Call, methodbuilder, null);
            }
            else
            {
                throw new NotSupportedException($"Cannot find func {funcCallNode.FunctionDeclaration.FullName} when calling.");
            }

        }

        public void GenerateDeclaration(VariableDeclaration declNode, ILGenerator ilGenerator)
        {
            LocalBuilder localBuilder = ilGenerator.DeclareLocal(declNode.ReturnType.ConvertToType());

            localBuilder.SetLocalSymInfo(declNode.Name);
            //add our local to dictionary
            varsLocalBuilders.Add(declNode.Name, localBuilder);

            if (declNode.Value != null)
            {
                GenerateExpression(declNode.Value, ilGenerator);
                //pop var from the stack and store in a value
                ilGenerator.Emit(OpCodes.Stloc, localBuilder);
            }

        }

        public void GenerateVarAssignment(VariableAssignment varAssignmentNode, ILGenerator ilGenerator)
        {
            GenerateExpression(varAssignmentNode.AssignmentValue, ilGenerator);

            PopAndStore(varAssignmentNode.VariableName.VariableDeclaration.FullName,
                varAssignmentNode.VariableName.VariableDeclaration.Index, ilGenerator);
        }

        public void GenerateArrayElementAssignment(ArrayElementAssignment arrayElementAssignmentNode, ILGenerator ilGenerator)
        {
            LoadOntoStack(arrayElementAssignmentNode.ArrayElementReference.Name, ilGenerator);

            //load arr index onto stack
            GenerateExpression(arrayElementAssignmentNode.ArrayElementReference.ArrayIndex, ilGenerator);
            //load value onto stack
            GenerateExpression(arrayElementAssignmentNode.AssignmentExpression, ilGenerator);

            if (arrayElementAssignmentNode.ArrayElementReference.ReturnType.ConvertToType() == typeof(char))
            {
                ilGenerator.Emit(OpCodes.Stelem_I2);
            }
            else
            {
                if (arrayElementAssignmentNode.ArrayElementReference.ReturnType.ConvertToType() == typeof(bool))
                {
                    ilGenerator.Emit(OpCodes.Stelem_I1);
                }
                else
                    ilGenerator.Emit(OpCodes.Stelem_I4);
            }
        }

        public void GenerateIfStatement(IfStatement ifStatementNode, ILGenerator ilGenerator)
        {
            //ifStatementNode.TrueCaseBlockStatement
            Label trueCase = ilGenerator.DefineLabel();
            Label falseCase = ilGenerator.DefineLabel();

            GenerateExpression(ifStatementNode.ConditionExpression, ilGenerator);

            //jump if false
            ilGenerator.Emit(OpCodes.Brfalse, falseCase);

            GenerateStatement(ifStatementNode.TrueCaseBlockStatement, ilGenerator);

            //unconditional
            ilGenerator.Emit(OpCodes.Br, trueCase);


            ilGenerator.MarkLabel(falseCase);

            if (ifStatementNode.FasleCaseBlockStatement != null)
            {
                GenerateStatement(ifStatementNode.FasleCaseBlockStatement, ilGenerator);
            }

            ilGenerator.MarkLabel(trueCase);
        }

        private void GenerateForStatement(ForStatement forStatement, ILGenerator ilGenerator)
        {
            Label conditionCheck = ilGenerator.DefineLabel();
            Label forBody = ilGenerator.DefineLabel();

            GenerateStatement(forStatement.InitializationStatement, ilGenerator);

            //unconditional
            ilGenerator.Emit(OpCodes.Br, conditionCheck);

            ilGenerator.MarkLabel(forBody);

            GenerateStatement(forStatement.BlockOrSingleStatement, ilGenerator);

            GenerateStatement(forStatement.IterationStatement, ilGenerator);

            ilGenerator.MarkLabel(conditionCheck);

            GenerateExpression(forStatement.ConditionExpression, ilGenerator);

            //jump if true
            ilGenerator.Emit(OpCodes.Brtrue, forBody);
        }

        public void GenerateWhileStatement(WhileStatement whileStatementNode, ILGenerator ilGenerator)
        {
            Label conditionCheck = ilGenerator.DefineLabel();
            Label whileBody = ilGenerator.DefineLabel();

            //unconditional
            ilGenerator.Emit(OpCodes.Br, conditionCheck);

            ilGenerator.MarkLabel(whileBody);

            GenerateStatement(whileStatementNode.BlockOrSingleStatement, ilGenerator);

            ilGenerator.MarkLabel(conditionCheck);

            GenerateExpression(whileStatementNode.ConditionExpression, ilGenerator);

            //jump if true
            ilGenerator.Emit(OpCodes.Brtrue, whileBody);
        }

        public void GenerateBlockStatement(BlockStatement blockStatementNode, ILGenerator ilGenerator)
        {
            foreach (var statement in blockStatementNode.Statements)
            {
                GenerateStatement(statement, ilGenerator);
            }
        }

        public void GenerateReturnStatement(ReturnStatement returnStatementNode, ILGenerator ilGenerator)
        {
            GenerateExpression(returnStatementNode.ReturnExpression, ilGenerator);
            ilGenerator.Emit(OpCodes.Ret);
        }

        #endregion

        private void GenerateExpression(IExpression iexpression, ILGenerator ilGenerator)
        {
            switch (iexpression)
            {
                case ExtendedId extendedId:
                    GenerateExtendedId(extendedId, ilGenerator);
                    break;
                case Expression expression:
                    GenerateExpression(expression, ilGenerator);
                    break;
                case Atom atom:
                    GenerateAtom(atom, ilGenerator);
                    break;
                case FunctionCall functionCall:
                    GenerateFuncCall(functionCall, ilGenerator);
                    break;
                case NewArray newArray:
                    GenerateNewArray(newArray, ilGenerator);
                    break;
                case ArrayElementReference arrayElementReference:
                    GenerateArrayElementReference(arrayElementReference, ilGenerator);
                    break;
                default: throw new ArgumentOutOfRangeException($"Cannot find {iexpression} in expression types");
            }
        }



        private void GenerateExtendedId(ExtendedId extendedIdNode, ILGenerator ilGenerator)
        {
            LoadOntoStack(extendedIdNode, ilGenerator);
        }

        private void GenerateAtom(Atom atomNode, ILGenerator ilGenerator)
        {
            //constant we put onto stack
            switch (atomNode)
            {
                case IntExpression intExpression:
                    ilGenerator.Emit(OpCodes.Ldc_I4, intExpression.Value);
                    break;
                case BoolExpression boolExpression:
                    ilGenerator.Emit(OpCodes.Ldc_I4, boolExpression.Value ? 1 : 0);
                    break;
                case CharExpression charExpression:
                    ilGenerator.Emit(OpCodes.Ldc_I4_S, charExpression.Value);
                    break;
                default:
                    throw new InvalidOperationException($"Bad value : {atomNode.Value}");
            }
        }

        private void GenerateNewArray(NewArray newArrayNode, ILGenerator ilGenerator)
        {
            //load arr size onto stack
            GenerateExpression(newArrayNode.ArraySize, ilGenerator);
            //new array
            ilGenerator.Emit(OpCodes.Newarr, newArrayNode.ReturnType.ConvertToType(false));
        }

        private void GenerateArrayElementReference(ArrayElementReference arrayElementReferenceNode,
            ILGenerator ilGenerator)
        {
            //load array
            LoadOntoStack(arrayElementReferenceNode.Name, ilGenerator);
            //load index
            GenerateExpression(arrayElementReferenceNode.ArrayIndex, ilGenerator);
            //load array element with index onto stack
            ilGenerator.Emit(OpCodes.Ldelem, arrayElementReferenceNode.ReturnType.ConvertToType());

        }

        private void GenerateExpression(Expression expressionNode, ILGenerator ilGenerator)
        {
            {
                GenerateExpression(expressionNode.Left, ilGenerator);

                if (expressionNode.Right != null)
                    GenerateExpression(expressionNode.Right, ilGenerator);

                switch (expressionNode.ExpressionType)
                {
                    case ExpressionType.Add:
                        ilGenerator.Emit(OpCodes.Add);
                        break;
                    case ExpressionType.Sub:
                        ilGenerator.Emit(OpCodes.Sub);
                        break;
                    case ExpressionType.Mul:
                        ilGenerator.Emit(OpCodes.Mul);
                        break;
                    case ExpressionType.Div:
                        ilGenerator.Emit(OpCodes.Div);
                        break;

                    case ExpressionType.Equal:
                        ilGenerator.Emit(OpCodes.Ceq);
                        break;
                    case ExpressionType.Greater:
                        ilGenerator.Emit(OpCodes.Cgt);
                        break;
                    case ExpressionType.EqualOrGreater:
                        ilGenerator.Emit(OpCodes.Clt);
                        ilGenerator.Emit(OpCodes.Ldc_I4_0);
                        ilGenerator.Emit(OpCodes.Ceq);
                        break;
                    case ExpressionType.Less:
                        ilGenerator.Emit(OpCodes.Clt);
                        break;
                    case ExpressionType.EqualOrLess:
                        ilGenerator.Emit(OpCodes.Cgt);
                        ilGenerator.Emit(OpCodes.Ldc_I4_0);
                        ilGenerator.Emit(OpCodes.Ceq);
                        break;
                    case ExpressionType.NotEqual:
                        ilGenerator.Emit(OpCodes.Ceq);
                        ilGenerator.Emit(OpCodes.Ldc_I4_0);
                        ilGenerator.Emit(OpCodes.Ceq);
                        break;
                    case ExpressionType.And:
                        ilGenerator.Emit(OpCodes.And);
                        break;
                    case ExpressionType.Or:
                        ilGenerator.Emit(OpCodes.Or);
                        break;
                    case ExpressionType.Not:
                        ilGenerator.Emit(OpCodes.Ldc_I4_0);
                        ilGenerator.Emit(OpCodes.Ceq);
                        break;

                }
            }

        }

        /// <summary>
        /// obsolete
        /// </summary>
        /// <param name="exId"></param>
        /// <param name="ilGenerator"></param>
        private void PopAndStore(ExtendedId exId, ILGenerator ilGenerator)
        {
            PopAndStore(exId.VariableDeclaration.FullName, exId.VariableDeclaration.Index, ilGenerator);
        }

        private void PopAndStore(string name, int? index, ILGenerator ilGenerator)
        {
            //local var
            if (varsLocalBuilders.Keys.Contains(name))
            {
                var localBuilder = varsLocalBuilders[name];
                //pop var from the stack and store in a value
                ilGenerator.Emit(OpCodes.Stloc, localBuilder);
            }
            //func argument var
            else if (index != null)
            {           //pop var from the stack and store in a func arg
                ilGenerator.Emit(OpCodes.Starg, (int)index);
            }
            else if (fieldBuilders.ContainsKey(name))
            {
                var fieldBuilder = fieldBuilders[name];

                ilGenerator.Emit(OpCodes.Stsfld, fieldBuilder);
            }
        }

        private void LoadOntoStack(ExtendedId extendedIdNode, ILGenerator ilGenerator)
        {
            if (varsLocalBuilders.Keys.Contains(extendedIdNode.VariableDeclaration.FullName))
            {
                var localBuilder = varsLocalBuilders[extendedIdNode.VariableDeclaration.FullName];

                ilGenerator.Emit(OpCodes.Ldloc, localBuilder);
            }
            else
            {
                if (extendedIdNode.VariableDeclaration != null)
                {
                    if (extendedIdNode.VariableDeclaration is FunctionVariableDeclarationParameter fdParam)
                    {
                        ilGenerator.Emit(OpCodes.Ldarg, (int)extendedIdNode.VariableDeclaration.Index);
                        return;
                    }

                }
            }
            if (fieldBuilders.ContainsKey(extendedIdNode.VariableDeclaration.FullName))
            {
                var fieldBuilder = fieldBuilders[extendedIdNode.VariableDeclaration.FullName];

                ilGenerator.Emit(OpCodes.Ldsfld, fieldBuilder);
            }
        }
    }
}

