using System;
using System.Diagnostics;
using SsmsSqlFormatter.Formatting;
using SsmsSqlFormatter.Options;

namespace FormatterBench
{
    class Program
    {
        static void Main(string[] args)
        {
            var sql = GenerateLargeSql();
            var options = new FormatterSettings { Preset = StylePreset.Modern, EnableFormattingCache = false };

            Console.WriteLine("Benchmarking ScriptDomFormatter: cold run (no cache)");
            var sw = Stopwatch.StartNew();
            var res = ScriptDomFormatter.Format(sql, options);
            sw.Stop();
            Console.WriteLine($"First run: {sw.ElapsedMilliseconds} ms, success={res.Success}");

            int iterations = 10;
            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                ScriptDomFormatter.Format(sql + i.ToString(), options); // slightly different to avoid cache
            }
            sw.Stop();
            Console.WriteLine($"{iterations} runs (unique inputs): {sw.ElapsedMilliseconds} ms total, avg={sw.ElapsedMilliseconds / (double)iterations} ms");

            Console.WriteLine("Now enabling formatting cache and re-running on same input");
            options.EnableFormattingCache = true;
            // prime cache
            ScriptDomFormatter.Format(sql, options);
            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                var r = ScriptDomFormatter.Format(sql, options);
            }
            sw.Stop();
            Console.WriteLine($"{iterations} cached runs: {sw.ElapsedMilliseconds} ms total, avg={sw.ElapsedMilliseconds / (double)iterations} ms");

            Console.WriteLine("Done");
        }

        static string GenerateLargeSql()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SELECT");
            for (int i = 0; i < 200; i++)
            {
                sb.AppendLine($"    col{i} AS c{i},");
            }
            sb.AppendLine("FROM (");
            sb.AppendLine("  SELECT 1 AS col0");
            sb.AppendLine(") t");
            sb.AppendLine("WHERE EXISTS (SELECT 1 FROM sys.objects o WHERE o.object_id = 1)");
            for (int j = 0; j < 50; j++) sb.AppendLine("-- comment " + j);
            return sb.ToString();
        }
    }
}
