﻿using System.Collections.Generic;

namespace JasminSharp
{
    public class ArgumentInstruction : IArgumentInstruction
    {
        public string Name { get; protected set; }
        public string Argument { get; protected set; }

        public virtual IEnumerable<string> GenerateListing()
        {
            var listing = new List<string> {$"{Name} {Argument}"};
            return listing;
        }
    }
}