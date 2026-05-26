namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Configurable rules for how successful deliveries are written at each margin tier.
    /// </summary>
    public sealed class DeliveryRules
    {
        public string Clean { get; }
        public string Strong { get; }
        public string Critical { get; }
        public string Exceptional { get; }
        public string Test { get; }
        public string RegisterInstruction { get; }
        public string MediumRule { get; }

        public DeliveryRules(string clean, string strong, string critical, string exceptional,
            string test, string registerInstruction, string mediumRule)
        {
            Clean = clean ?? "";
            Strong = strong ?? "";
            Critical = critical ?? "";
            Exceptional = exceptional ?? "";
            Test = test ?? "";
            RegisterInstruction = registerInstruction ?? "";
            MediumRule = mediumRule ?? "";
        }
    }
}
