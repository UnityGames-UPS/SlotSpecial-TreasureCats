using System;
using System.Collections.Generic;
using Newtonsoft.Json;
#region Server Communication Models

// Root deserialization class from server game:init payload
[Serializable]
public class Root
{
    public string id { get; set; }
    public GameData gameData { get; set; }
    public Features features { get; set; }
    public UiData uiData { get; set; }
    public Player player { get; set; }
}

[Serializable]
public class GameData
{
    public List<List<int>> lines { get; set; }
    public List<double> bets { get; set; }
    public int totalLines { get; set; }
}

[Serializable]
public class Features
{
    public Scatter scatter { get; set; }
    public FreeGames freeGames { get; set; }
    public TreasureHunt treasureHunt { get; set; }
    public WildSubstitution wildSubstitution { get; set; }
}

[Serializable]
public class Scatter
{
    public bool enabled { get; set; }
    public int minTriggerCount { get; set; }
    public int scatterSymbolId { get; set; }
}

[Serializable]
public class FreeGames
{
    public bool enabled { get; set; }
    public bool retrigger { get; set; }
    public bool stickyWilds { get; set; }
    public SpinsByCount spinsByCount { get; set; }
}

[Serializable]
public class SpinsByCount
{
    [JsonProperty("3")]
    public int _3 { get; set; }

    [JsonProperty("4")]
    public int _4 { get; set; }

    [JsonProperty("5")]
    public int _5 { get; set; }
}

[Serializable]
public class TreasureHunt
{
    public Wheel wheel { get; set; }
    public bool enabled { get; set; }
    public List<string> mapTiles { get; set; }
    public List<int> coinPrizes { get; set; }
    public int bonusSymbolId { get; set; }
    public List<int> requiredReels { get; set; }
    public int minTriggerCount { get; set; }
    public bool disabledDuringFreeGames { get; set; }
}

[Serializable]
public class Wheel
{
    public List<int> coinPrizes { get; set; }
    public List<int> diceThrows { get; set; }
}

[Serializable]
public class WildSubstitution
{
    public bool enabled { get; set; }
    public List<int> wildSymbolIds { get; set; }
    public List<int> substituteAllExcept { get; set; }
}

[Serializable]
public class UiData
{
    public Paylines paylines { get; set; }
}

[Serializable]
public class Paylines
{
    public List<Symbol> symbols { get; set; }
}

[Serializable]
public class Symbol
{
    public int id { get; set; }
    public string name { get; set; }
    public string description { get; set; }
    public Dictionary<string, double> payout { get; set; }
}

[Serializable]
public class Player
{
    public double balance { get; set; }
}

#endregion