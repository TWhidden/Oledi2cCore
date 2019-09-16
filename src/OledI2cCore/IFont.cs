using System.Collections.Generic;

namespace OledI2cCore
{
    public interface IFont
    {
        bool MonoSpace { get; }
        byte Width { get; }
        byte Height { get; }
        IReadOnlyDictionary<char, byte[]> FontData { get; }
    }
}
