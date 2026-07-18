using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.Core.Tests
{
    public class ArchitectureRuleTests
    {
        // ── Test A ────────────────────────────────────────────────────────────

        [Fact]
        public void KernelPurity()
        {
            var assembly = typeof(GameSession).Assembly;
            var referencedAssemblies = assembly.GetReferencedAssemblies();
            var offending = new List<string>();

            foreach (var refAsm in referencedAssemblies)
            {
                var name = refAsm.Name;
                if (name == null) continue;

                bool isPermitted = name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("System", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Microsoft.Bcl.", StringComparison.OrdinalIgnoreCase);

                if (!isPermitted)
                {
                    offending.Add(name);
                }
            }

            if (offending.Count > 0)
            {
                Assert.Fail($"Pinder.Core must remain a domain kernel with only framework/BCL-support dependencies. Offending assemblies: {string.Join(", ", offending)}");
            }
        }

        [Fact]
        public void CoreAssembly_DoesNotExposeRuntimeDataFileLocator()
        {
            var assembly = typeof(GameSession).Assembly;
            Assert.Null(assembly.GetType("Pinder.Core.Data.DataFileLocator"));
        }

        [Fact]
        public void DefaultRuleResolver_HasNoCoreOwnedDefaultType()
        {
            var coreAssembly = typeof(GameSession).Assembly;
            Assert.Null(coreAssembly.GetType("Pinder.Core.Interfaces.CoreDefaultRuleResolver"));

            var resolver = DefaultRuleResolver.Instance;
            if (resolver != null)
            {
                Assert.NotEqual(coreAssembly, resolver.GetType().Assembly);
            }
        }

        // ── Test B ────────────────────────────────────────────────────────────

        [Fact]
        public void SingleProductionAdapter()
        {
            var assembly = typeof(PinderLlmAdapter).Assembly;
            var adapterTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface &&
                            (typeof(ILlmAdapter).IsAssignableFrom(t) ||
                             typeof(IStatefulLlmAdapter).IsAssignableFrom(t)))
                .ToList();

            var expectedFullName = "Pinder.LlmAdapters.PinderLlmAdapter";
            var extraTypes = adapterTypes
                .Where(t => t.FullName != expectedFullName)
                .Select(t => t.FullName ?? t.Name)
                .ToList();

            if (adapterTypes.Count != 1 || adapterTypes[0].FullName != expectedFullName)
            {
                Assert.Fail("second production ILlmAdapter implementation detected — parallel adapter paths are how the OverlayApplier dead-end happened; extend PinderLlmAdapter/ILlmTransport instead. Extra types found: " + string.Join(", ", extraTypes));
            }
        }

        // ── Test C ────────────────────────────────────────────────────────────

        private static readonly HashSet<string> DecoratorAllowlist = new HashSet<string>
        {
            // #340 cosmetic punctuation decorator, wraps inner ILlmTransport
            "Pinder.LlmAdapters.PunctuationNormalizingTransport",

            // #831 thinking-block strip decorator, wraps inner ILlmTransport
            "Pinder.LlmAdapters.ThinkingStrippingLlmTransport"
        };

        [Fact]
        public void TransportNamespaceShape()
        {
            var assembly = typeof(PinderLlmAdapter).Assembly;
            var transportTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(ILlmTransport).IsAssignableFrom(t))
                .ToList();

            foreach (var type in transportTypes)
            {
                var ns = type.Namespace;
                var fullName = type.FullName;

                bool isVendorNamespace = ns == "Pinder.LlmAdapters.Anthropic" || ns == "Pinder.LlmAdapters.OpenAi";
                bool isAllowlisted = fullName != null && DecoratorAllowlist.Contains(fullName);

                if (!isVendorNamespace && !isAllowlisted)
                {
                    Assert.Fail($"Offending transport type '{fullName}' with namespace '{ns}'. New VENDOR transports must live under a vendor namespace (Pinder.LlmAdapters.<Vendor>) and new cross-cutting decorators must be added to the allowlist with justification.");
                }
            }
        }
    }
}
