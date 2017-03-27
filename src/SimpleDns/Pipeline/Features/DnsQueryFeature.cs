using System;

namespace SimpleDns.src.SimpleDns.Pipeline.Features {
    public interface IDnsQueryFeature {
        string Name { get; }
        UInt16 Type { get; }
        UInt16 Class { get; }
        Int32 Size { get; }
    }

    public class DnsQueryFeature : IDnsQueryFeature {
        public string Name { get; }
        public UInt16 Type { get; }
        public UInt16 Class { get; }
        public Int32 Size { get; }

        public DnsQueryFeature(string name, UInt16 type, UInt16 @class, Int32 size) {
            Name = name;
            Type = type;
            Class = @class;
            Size = size;
        }
    }
}

