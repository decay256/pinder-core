using System;
using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    public static class BackstoryValidator
    {
        public static readonly IReadOnlyList<string> RequiredCategories = new[]
        {
            "age_and_demographics",
            "birthplace_and_origin",
            "childhood_milieu",
            "parental_dynamics",
            "early_education_scars",
            "higher_education",
            "formative_intimacies",
            "career_debut",
            "current_profession",
            "financial_hygiene",
            "domestic_milieu",
            "social_circle",
            "recent_ex",
            "career_low",
            "delusional_plan",
            "hyperfixations",
            "ideological_posture",
            "digital_footprint",
            "physical_dysmorphia",
            "dependencies"
        };

        public static bool Validate(Dictionary<string, BackstoryFact> facts)
        {
            if (facts == null) return false;
            if (facts.Count != RequiredCategories.Count) return false;

            foreach (var category in RequiredCategories)
            {
                if (!facts.TryGetValue(category, out var fact) || fact == null)
                    return false;
                
                if (string.IsNullOrWhiteSpace(fact.BioLie)) return false;
                if (string.IsNullOrWhiteSpace(fact.TragicReality)) return false;
            }

            return true;
        }
    }
}