using System;
using Pinder.Core.Stats;

partial class Program
{
    internal static void PrintSetupDetails(GameSetupResult result)
    {
        // ── character table ───────────────────────────────────────────────
        Console.WriteLine("## Characters");
        Console.WriteLine();
        Console.WriteLine($"***{result.Player1} bio:*** *\"{result.Sable.Bio}\"*");
        Console.WriteLine();
        Console.WriteLine($"***{result.Player2} bio:*** *\"{result.Brick.Bio}\"*");
        Console.WriteLine();
        Console.WriteLine($"| | **{result.Player1}** | **{result.Player2}** |");
        Console.WriteLine("|---|---|---|");
        Console.WriteLine($"| Level | {result.P1Level} | {result.P2Level} |");
        foreach (var stat in new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness }) {
            int p1 = result.SableStats.GetEffective(stat), p2 = result.BrickStats.GetEffective(stat);
            Console.WriteLine($"| {StatLabel(stat)} | {p1:+#;-#;0} | {p2:+#;-#;0} |");
        }
        Console.WriteLine();

        // ── DC table ──────────────────────────────────────────────────────
        string player1 = result.Player1;
        string player2 = result.Player2;
        Console.WriteLine($"## DC Reference ({player1} attacking, {player2} defending)");
        Console.WriteLine();
        Console.WriteLine($"| Stat | {player1} mod | {player2} defends with | DC | Need | % | Risk |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var stat in new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness }) {
            int atkMod = result.SableStats.GetEffective(stat);
            StatType defStat = Pinder.Core.Stats.StatBlock.DefenceTable[stat];
            int defMod = result.BrickStats.GetEffective(defStat);
            int dc = result.BrickStats.GetDefenceDC(stat);
            int need = dc - (atkMod + result.P1LevelBonus); // include level bonus
            // need ≥20 = Reckless (only Nat 20 succeeds = 5%); else standard formula
            int pct = need >= 20 ? 5 : Math.Max(0, Math.Min(100, (21 - need) * 5));
            Console.WriteLine($"| {StatLabel(stat)} | {atkMod:+#;-#;0} | {StatLabel(defStat)} {defMod:+#;-#;0} | {dc} | {need}+ | {pct}% | {RiskLabel(need)} |");
        }
        Console.WriteLine();
        Console.WriteLine("> DC = 16 + opponent defending stat modifier. Miss by 1–2 = Fumble | 3–5 = Misfire | 6–9 = Trope Trap | 10+ = Catastrophe | Nat 1 = Legendary.");
        Console.WriteLine();

        // ── archetype directives ──────────────────────────────────────────
        bool hasP1Archetype = result.Sable.ActiveArchetype != null;
        bool hasP2Archetype = result.Brick.ActiveArchetype != null;
        if (hasP1Archetype || hasP2Archetype)
        {
            Console.WriteLine("### Archetype Directives");
            Console.WriteLine();
            if (hasP1Archetype)
            {
                Console.WriteLine($"**{result.Player1} ({result.Sable.ActiveArchetype!.Name} — {result.Sable.ActiveArchetype.InterferenceLevel}):**");
                foreach (var directiveLine in result.Sable.ActiveArchetype.Behavior.Split('\n'))
                    Console.WriteLine($"> {directiveLine}");
                Console.WriteLine();
            }
            if (hasP2Archetype)
            {
                Console.WriteLine($"**{result.Player2} ({result.Brick.ActiveArchetype!.Name} — {result.Brick.ActiveArchetype.InterferenceLevel}):**");
                foreach (var directiveLine in result.Brick.ActiveArchetype.Behavior.Split('\n'))
                    Console.WriteLine($"> {directiveLine}");
                Console.WriteLine();
            }
        }

        // ── steering roll explanation ─────────────────────────────────────
        int steeringMod = (result.SableStats.GetEffective(StatType.Charm) + result.SableStats.GetEffective(StatType.Wit) + result.SableStats.GetEffective(StatType.SelfAwareness)) / 3;
        int steeringDC = 16 + (result.BrickStats.GetEffective(StatType.SelfAwareness) + result.BrickStats.GetEffective(StatType.Rizz) + result.BrickStats.GetEffective(StatType.Honesty)) / 3;
        Console.WriteLine($"> 🧭 **Steering**: After each delivery, {result.Player1} may append a follow-up sentence.");
        Console.WriteLine($"> Roll: d20 + (CHARM+WIT+SA)/3 = +{steeringMod} vs DC = 16 + (opponent SA+RIZZ+HONESTY)/3 = {steeringDC}");
        Console.WriteLine("> On success: adds a steering question. No interest effect — purely narrative.");
        Console.WriteLine();
    }
}
