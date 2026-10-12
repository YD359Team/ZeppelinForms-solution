namespace ZF_SharedLib.Models;

/// <summary>The data the showcase's pages show: the same games in the grid, the
/// tree and the lists, so that one can be compared with another.</summary>
internal static class Samples
{
    public static readonly Game[] Games =
    [
        new("Half-Life 2", "Valve", 2004, 9.6f, true),
        new("Portal 2", "Valve", 2011, 9.5f, true),
        new("The Witcher 3", "CD Projekt Red", 2015, 9.3f, true),
        new("Disco Elysium", "ZA/UM", 2019, 9.1f, false),
        new("Hollow Knight", "Team Cherry", 2017, 9.0f, true),
        new("Factorio", "Wube Software", 2020, 9.4f, false),
        new("Outer Wilds", "Mobius Digital", 2019, 8.9f, true),
        new("Return of the Obra Dinn", "Lucas Pope", 2018, 8.8f, true),
        new("Slay the Spire", "Mega Crit", 2019, 8.7f, false),
        new("Celeste", "Maddy Makes Games", 2018, 9.0f, true),
        new("Hades", "Supergiant Games", 2020, 9.2f, false),
        new("Subnautica", "Unknown Worlds", 2018, 8.6f, true),
        new("RimWorld", "Ludeon Studios", 2018, 9.0f, false),
        new("Stardew Valley", "ConcernedApe", 2016, 8.9f, true),
        new("Dishonored", "Arkane Studios", 2012, 8.8f, true),
        new("Prey", "Arkane Studios", 2017, 8.5f, false),
        new("Divinity: Original Sin 2", "Larian Studios", 2017, 9.2f, true),
        new("Baldur's Gate 3", "Larian Studios", 2023, 9.6f, false),
        new("Katana ZERO", "Askiisoft", 2019, 8.7f, true),
        new("Tunic", "Andrew Shouldice", 2022, 8.4f, false),
    ];

    public static readonly string[] Countries =
    [
        "Argentina", "Armenia", "Australia", "Austria", "Belgium", "Brazil", "Bulgaria",
        "Canada", "Chile", "China", "Croatia", "Cyprus", "Czechia", "Denmark", "Egypt",
        "Estonia", "Finland", "France", "Georgia", "Germany", "Greece", "Hungary",
        "Iceland", "India", "Indonesia", "Ireland", "Israel", "Italy", "Japan",
        "Kazakhstan", "Latvia", "Lithuania", "Mexico", "Mongolia", "Netherlands",
        "New Zealand", "Norway", "Poland", "Portugal", "Romania", "Russia", "Serbia",
        "Singapore", "Slovakia", "Slovenia", "South Korea", "Spain", "Sweden",
        "Switzerland", "Thailand", "Turkey", "Ukraine", "United Kingdom",
        "United States", "Uruguay", "Uzbekistan", "Vietnam",
    ];
}
