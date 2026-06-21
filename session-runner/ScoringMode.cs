namespace Pinder.SessionRunner
{
    /// <summary>
    /// Describes how an agent's scoring was derived (engine, heuristic, llm, or human).
    /// </summary>
    public enum ScoringMode
    {
        Engine,
        Heuristic,
        Llm,
        Human
    }
}
