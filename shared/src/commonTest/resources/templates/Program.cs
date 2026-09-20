using System;
using System.Collections.Generic;

namespace Glance
{
    public class Config
    {
        public string Name { get; set; }
        public int Version { get; set; }

        public Config(string name, int version)
        {
            Name = name;
            Version = version;
        }

        public override string ToString()
        {
            return $"Config{{{Name} v{Version}}}";
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var config = new Config("Glance", 1);
            var names = new List<string> { config.Name };
            Console.WriteLine(config);
        }
    }
}
