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
            Console.Error.WriteLine($"Ни один сценарий не подошёл под фильтр '{options.Filter}'.");
            return 1;
        }

        Console.WriteLine($"Платформа: {Baseline.CurrentPlatform}, сценариев: {benchmarks.Count}");
        Console.WriteLine($"Режим GC: {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}");
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
            Console.WriteLine($"Результаты записаны: {jsonPath}");
        }

        string baselinePath = options.BaselinePath ?? Baseline.DefaultPath(options.BaselineDirectory);

        if (options.UpdateBaseline)
        {
            Baseline.Save(baselinePath, results);
            Console.WriteLine($"Эталон обновлён: {baselinePath}");
            return 0;
        }

        if (!options.CheckBaseline)
            return 0;

        BaselineFile? baseline = Baseline.Load(baselinePath);

        if (baseline is null)
        {
            Console.Error.WriteLine(
                $"Эталон не найден: {baselinePath}. " +
                "Создайте его командой --update-baseline и закоммитьте.");

            return 1;
        }

        List<Regression> regressions = Baseline.Compare(
            baseline, results, options.TimeTolerance, options.AllocationTolerance);

        if (regressions.Count == 0)
        {
            Console.WriteLine("Регрессий нет.");
            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Регрессий: {regressions.Count}");

        foreach (Regression regression in regressions)
        {
            Console.Error.WriteLine(
                $"  {regression.Scenario} / {regression.Metric}: " +
                $"было {regression.Baseline:F2}, стало {regression.Current:F2} " +
                $"({Format.Percent(regression.Ratio)})");
        }

        return 1;
    }

    private static void PrintDetails(IEnumerable<BenchmarkResult> results)
    {
        Console.WriteLine(
            $"{"Сценарий",-34}{"медиана",10}{"p95",10}{"аллок/итер",14}" +
            $"{"удержано",12}{"WS дельта",12}{"GC 0/1/2",12}");

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
                        options.Iterations = int.Parse(Next(args, ref i));
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
                        options.TimeTolerance = double.Parse(Next(args, ref i));
                        break;

                    case "--alloc-tolerance":
                        options.AllocationTolerance = double.Parse(Next(args, ref i));
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
                throw new ArgumentException($"Для {args[index - 1]} не задано значение.");

            return args[index];
        }

        public static void PrintUsage()
        {
            Console.WriteLine("""
                ZeppelinForms.Benchmarks

                  -f, --filter <текст>        запустить только сценарии с этой подстрокой в имени
                  -n, --iterations <число>    переопределить число итераций для всех сценариев
                      --json <путь>           записать результаты в файл
                      --baseline <путь>       путь к эталону (иначе Baselines/baseline-<ось>.json)
                      --baseline-dir <путь>   каталог эталонов
                      --update-baseline       перезаписать эталон текущими результатами
                      --check                 сравнить с эталоном; код возврата 1 при регрессии
                      --time-tolerance <доля> допустимый рост медианы, по умолчанию 0.20
                      --alloc-tolerance <доля> допустимый рост аллокаций, по умолчанию 0.05
                  -h, --help                  эта справка
                """);
        }
    }
}