﻿using System.Collections.Generic;
using System.Linq;
using Antlr.Runtime.Tree;
using MathLang.Extensions;

namespace MathLang.Tree.Nodes
{
    public class FunctionCall : IExpression, IStatement
    {
        public INode Parent { get; }

        public string Name { get; set; }
        public List<IExpression> FunctionCallParameters { get; } = new List<IExpression>();

        public FunctionCall(INode parent)
        {
            Parent = parent;
        }

        public void Construct(CommonTree syntaxFuncCallExpression)
        {
            var syntaxName = syntaxFuncCallExpression.GetChild(0).CastTo<CommonTree>();
            Name = syntaxName.Text;
            if (syntaxName.ChildCount > 0)
            {
                Name += "." + syntaxName.GetChild(0).Text;
            }

            if (syntaxFuncCallExpression.ChildCount > 1)
            {
                syntaxFuncCallExpression.GetChild(1).CastTo<CommonTree>()
                    .Children.Cast<CommonTree>()
                    .ForEach(syntaxfuncCallParam =>
                    {
                        IExpression parameter = TreeHelper.GetExpression(this, syntaxfuncCallParam);             
                        parameter.Construct(syntaxfuncCallParam);
                        FunctionCallParameters.Add(parameter);
                    });
            }
        }
    }
}