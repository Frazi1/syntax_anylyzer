﻿using System.Collections.Generic;

namespace MathLang.CodeGeneration.JasminJava
{
    public class NoArgumentInstruction : IInstruction
    {
        public string Name { get; protected set; }

        public IEnumerable<string> GenerateListing()
        {
            var listing = new List<string> { $"{Name}" };
            return listing;
        }
    }
}