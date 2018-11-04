// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Schemy
{
    using System;
    using System.IO;

    public static class Program
    {
        public static void Main(string[] args)
        {
            PrettyPriner.defaultOutput = Console.Out;

            if (args.Length > 0 && File.Exists(args[0]))
            {
                // evaluate input file's content
                var file = args[0];
                var interpreter = new Interpreter(null, new ReadOnlyFileSystemAccessor());

                using (TextReader reader = new StreamReader(file))
                {
                    object res = interpreter.Evaluate(reader);
                    Console.WriteLine(Utils.PrintExpr(res));
                }
            }
            else
            {
                // starts the REPL
                var interpreter = new Interpreter(null, new ReadOnlyFileSystemAccessor());
                var headers = new[]
                {
                    "-----------------------------------------------",
                    "| Schemy - Scheme as a Configuration Language |",
                    "| Press Ctrl-C to exit                        |",
                    "-----------------------------------------------",
                };
                var stingReader = new StringReader("(print-linear '(1 2 3) #t)");
                var result = interpreter.Evaluate(stingReader);

                interpreter.REPL(Console.In, Console.Out, "Schemy> ", headers);
            }
        }
    }
}
