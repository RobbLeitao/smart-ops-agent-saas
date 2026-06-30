using System;

namespace Microsoft.SemanticKernel.SkillDefinition
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class KernelFunctionAttribute : Attribute
    {
        public KernelFunctionAttribute()
        {
        }
    }
}
