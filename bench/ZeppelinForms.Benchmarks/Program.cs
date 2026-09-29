using System.Globalization;

namespace ZeppelinForms.Benchmarks;

public static class Program
{
    public static int Main(string[] args)
    {
        var options = Options.Parse(args);

        if (options.ShowHelp)
        {
            Options.PrintUsage();
            return 0;
        }

        List<Benchmark> benchmarks = Scenarios.All()
            .Where(b => options.Filter is null ||
                        b.Name.Contains(options.Filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (benchmarks.Count == 0)
        {
            Console.Error.WriteLine($"No scenario matched the filter '{options.Filter}'.");
            return 1;
        }

        Console.WriteLine($"Platform: {Baseline.CurrentPlatform}, scenarios: {benchmarks.Count}");
        Console.WriteLine($"GC mode: {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}");
        Console.WriteLine();

        var results = new List<BenchmarkResult>();

        foreach (Benchmark benchmark in benchmarks)
        {
            Console.Write($"  {benchmark.Name,-34}");

            BenchmarkResult result = BenchmarkRunner.Run(benchmark, options.Iterations);
            results.Add(result);

            Console.WriteLine(result.ShortSummary);
        }

        Console.WriteLine();
        PrintDetails(results);

        if (options.JsonPath is { } jsonPath)
        {
            Baseline.Save(jsonPath, results);
            Console.WriteLine($"Results written: {jsonPath}");
        }

        string baselinePath = options.BaselinePath ?? Baseline.DefaultPath(options.BaselineDirectory);

        if (options.UpdateBaseline)
        {
            Baseline.Save(baselinePath, results);
            Console.WriteLine($"Baseline updated: {baselinePath}");
            return 0;
        }

        if (!options.CheckBaseline)
            return 0;

        BaselineFile? baseline = Baseline.Load(baselinePath);

        if (baseline is null)
        {
            Console.Error.WriteLine(
                $"Baseline not found: {baselinePath}. " +
                "Create it with --update-baseline and commit it.");

            return 1;
        }

        List<Regression> regressions = Baseline.Compare(
            baseline, results, options.TimeTolerance, options.AllocationTolerance);

        if (regressions.Count == 0)
        {
            Console.WriteLine("No regressions.");
            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Regressions: {regressions.Count}");

        foreach (Regression regression in regressions)
        {
            Console.Error.WriteLine(
                $"  {regression.Scenario} / {regression.Metric}: " +
                $"was {regression.Baseline:F2}, now {regression.Current:F2} " +
                $"({Format.Percent(regression.Ratio)})");
        }

        return 1;
    }

    private static void PrintDetails(IEnumerable<BenchmarkResult> results)
    {
        Console.WriteLine(
            $"{"Scenario",-34}{"median",10}{"p95",10}{"alloc/iter",14}" +
            $"{"retained",12}{"WS delta",12}{"GC 0/1/2",12}");

        Console.WriteLine(new string('-', 104));

        foreach (BenchmarkResult r in results)
        {
            Console.WriteLine(
                $"{r.Name,-34}{r.MedianMs,10:F3}{r.P95Ms,10:F3}" +
                $"{Format.Bytes(r.AllocatedBytesPerIteration),14}" +
                $"{Format.Bytes(r.RetainedBytes),12}" +
                $"{Format.Bytes(r.WorkingSetDeltaBytes),12}" +
                $"{$"{r.Gen0Collections}/{r.Gen1Collections}/{r.Gen2Collections}",12}");
        }

        Console.WriteLine();
    }

    private sealed class Options
    {
        public string? Filter { get; private set; }
        public int? Iterations { get; private set; }
        public string? JsonPath { get; private set; }
        public string? BaselinePath { get; private set; }
        public string BaselineDirectory { get; private set; } = "Baselines";
        public bool UpdateBaseline { get; private set; }
        public bool CheckBaseline { get; private set; }
        public double TimeTolerance { get; private set; } = 0.20;
        public double AllocationTolerance { get; private set; } = 0.05;
        public bool ShowHelp { get; private set; }

        /// <remarks>
        /// Numbers are parsed in the invariant culture, as command-line arguments
        /// should be. The current culture used to be used, and on a machine with
        /// a comma decimal separator "--time-tolerance 0.3" threw FormatException.
        /// </remarks>
        public static Options Parse(string[] args)
        {
            var options = new Options();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--filter" or "-f":
                        options.Filter = Next(args, ref i);
                        break;

                    case "--iterations" or "-n":
                        options.Iterations = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;

                    case "--json":
                        options.JsonPath = Next(args, ref i);
                        break;

                    case "--baseline":
                        options.BaselinePath = Next(args, ref i);
                        break;

                    case "--baseline-dir":
                        options.BaselineDirectory = Next(args, ref i);
                        break;

                    case "--update-baseline":
                        options.UpdateBaseline = true;
                        break;

                    case "--check":
                        options.CheckBaseline = true;
                        break;

                    case "--time-tolerance":
                        options.TimeTolerance = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;

                    case "--alloc-tolerance":
                        options.AllocationTolerance = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;

                    case "--help" or "-h":
                        options.ShowHelp = true;
                        break;
                }
            }

            return options;
        }

        private static string Next(string[] args, ref int index)
        {
            if (++index >= args.Length)
                throw new ArgumentException($"No value given for {args[index - 1]}.");

            return args[index];
        }

        public static void PrintUsage()
        {
            Console.WriteLine("""
                ZeppelinForms.Benchmarks

                  -f, --filter <text>          run only scenarios with this substring in the name
                  -n, --iterations <number>    override the number of iterations for all scenarios
                      --json <path>            write the results to a file
                      --baseline <path>        path to the baseline (otherwise Baselines/baseline-<platform>.json)
                      --baseline-dir <path>    the baselines directory
                      --update-baseline        overwrite the baseline with the current results
                      --check                  compare with the baseline; exit code 1 on a regression
                      --time-tolerance <ratio> allowed growth of the median, 0.20 by default
                      --alloc-tolerance <ratio> allowed growth of allocations, 0.05 by default
                  -h, --help                   this help
                """);
        }
    }
}