﻿using System;
using Antlr.Runtime.Tree;
using MathLang.Tree.Nodes;

namespace MathLang.Tree
{
    public class DoWhileStatement: IStatement
    {
        public INode Parent { get; }

        public DoWhileStatement(INode parent)
        {
            Parent = parent;
        }

        public void Construct(CommonTree tree)
        {
            throw new NotImplementedException();
        }

    }
}