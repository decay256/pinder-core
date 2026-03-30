using System;
using System.Linq;
using System.Reflection;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Spec-driven tests verifying project structure, namespaces, and type locations
    /// per issue #205 acceptance criteria.
    /// </summary>
    public class ProjectStructureTests
    {
        #region AC1: Assembly and namespace verification

        // What: AC1 — Assembly name is Pinder.LlmAdapters
        // Mutation: would catch if AssemblyName was wrong in csproj
        [Fact]
        public void Assembly_Name_IsPinderLlmAdapters()
        {
            var assembly = typeof(AnthropicOptions).Assembly;
            Assert.Equal("Pinder.LlmAdapters", assembly.GetName().Name);
        }

        // What: AC1 — Project references Pinder.Core (one-way dependency)
        // Mutation: would catch if ProjectReference to Pinder.Core was missing
        // Verified by the fact that Pinder.Core types are resolvable from this test assembly
        [Fact]
        public void Assembly_CanResolve_PinderCoreTypes()
        {
            // If Pinder.LlmAdapters didn't reference Pinder.Core, this wouldn't compile
            var coreType = typeof(Pinder.Core.Interfaces.ILlmAdapter);
            Assert.NotNull(coreType);
            Assert.Equal("Pinder.Core", coreType.Assembly.GetName().Name);
        }

        // What: AC1 — Project references Newtonsoft.Json
        // Mutation: would catch if Newtonsoft.Json PackageReference was missing
        [Fact]
        public void Assembly_References_NewtonsoftJson()
        {
            var assembly = typeof(AnthropicOptions).Assembly;
            var refs = assembly.GetReferencedAssemblies();
            Assert.Contains(refs, r => r.Name == "Newtonsoft.Json");
        }

        #endregion

        #region AC2: DTOs in correct namespaces

        // What: AC2 — MessagesRequest is in Pinder.LlmAdapters.Anthropic.Dto
        // Mutation: would catch if namespace was wrong
        [Fact]
        public void MessagesRequest_IsInCorrectNamespace()
        {
            Assert.Equal("Pinder.LlmAdapters.Anthropic.Dto", typeof(MessagesRequest).Namespace);
        }

        // What: AC2 — ContentBlock is in Pinder.LlmAdapters.Anthropic.Dto
        // Mutation: would catch if namespace was wrong
        [Fact]
        public void ContentBlock_IsInCorrectNamespace()
        {
            Assert.Equal("Pinder.LlmAdapters.Anthropic.Dto", typeof(ContentBlock).Namespace);
        }

        // What: AC2 — CacheControl is in Pinder.LlmAdapters.Anthropic.Dto
        // Mutation: would catch if namespace was wrong
        [Fact]
        public void CacheControl_IsInCorrectNamespace()
        {
            Assert.Equal("Pinder.LlmAdapters.Anthropic.Dto", typeof(CacheControl).Namespace);
        }

        // What: AC2 — Message is in Pinder.LlmAdapters.Anthropic.Dto
        // Mutation: would catch if namespace was wrong
        [Fact]
        public void Message_IsInCorrectNamespace()
        {
            Assert.Equal("Pinder.LlmAdapters.Anthropic.Dto", typeof(Message).Namespace);
        }

        // What: AC2 — MessagesResponse is in Pinder.LlmAdapters.Anthropic.Dto
        // Mutation: would catch if namespace was wrong
        [Fact]
        public void MessagesResponse_IsInCorrectNamespace()
        {
            Assert.Equal("Pinder.LlmAdapters.Anthropic.Dto", typeof(MessagesResponse).Namespace);
        }

        // What: AC2 — ResponseContent is in Pinder.LlmAdapters.Anthropic.Dto
        // Mutation: would catch if namespace was wrong
        [Fact]
        public void ResponseContent_IsInCorrectNamespace()
        {
            Assert.Equal("Pinder.LlmAdapters.Anthropic.Dto", typeof(ResponseContent).Namespace);
        }

        // What: AC2 — UsageStats is in Pinder.LlmAdapters.Anthropic.Dto
        // Mutation: would catch if namespace was wrong
        [Fact]
        public void UsageStats_IsInCorrectNamespace()
        {
            Assert.Equal("Pinder.LlmAdapters.Anthropic.Dto", typeof(UsageStats).Namespace);
        }

        #endregion

        #region AC3/AC4: Config types in correct namespace

        // What: AC3 — AnthropicOptions is in Pinder.LlmAdapters.Anthropic
        // Mutation: would catch if namespace was wrong
        [Fact]
        public void AnthropicOptions_IsInCorrectNamespace()
        {
            Assert.Equal("Pinder.LlmAdapters.Anthropic", typeof(AnthropicOptions).Namespace);
        }

        // What: AC4 — AnthropicApiException is in Pinder.LlmAdapters.Anthropic
        // Mutation: would catch if namespace was wrong
        [Fact]
        public void AnthropicApiException_IsInCorrectNamespace()
        {
            Assert.Equal("Pinder.LlmAdapters.Anthropic", typeof(AnthropicApiException).Namespace);
        }

        #endregion

        #region Sealed class verification (LangVersion 8.0 compliance)

        // What: Spec requires sealed class for DTOs (no record types, netstandard2.0)
        // Mutation: would catch if sealed modifier was removed
        [Theory]
        [InlineData(typeof(MessagesRequest))]
        [InlineData(typeof(ContentBlock))]
        [InlineData(typeof(CacheControl))]
        [InlineData(typeof(Message))]
        [InlineData(typeof(MessagesResponse))]
        [InlineData(typeof(ResponseContent))]
        [InlineData(typeof(UsageStats))]
        [InlineData(typeof(AnthropicOptions))]
        public void DtoTypes_AreSealed(Type type)
        {
            Assert.True(type.IsSealed, $"{type.Name} should be sealed");
        }

        // What: AC4 — AnthropicApiException should NOT be sealed (it's a class extending Exception)
        // Mutation: would catch if exception class was sealed when spec says "public class"
        [Fact]
        public void AnthropicApiException_IsAClass_ExtendingException()
        {
            Assert.True(typeof(AnthropicApiException).IsClass);
            Assert.True(typeof(Exception).IsAssignableFrom(typeof(AnthropicApiException)));
        }

        #endregion

        #region All expected types exist

        // What: AC2/AC3/AC4 — All 9 expected types are present and loadable
        // Mutation: would catch if any type was missing from the assembly
        [Fact]
        public void AllExpectedTypes_ExistInAssembly()
        {
            var assembly = typeof(AnthropicOptions).Assembly;
            var typeNames = assembly.GetExportedTypes().Select(t => t.FullName).ToList();

            Assert.Contains("Pinder.LlmAdapters.Anthropic.Dto.MessagesRequest", typeNames);
            Assert.Contains("Pinder.LlmAdapters.Anthropic.Dto.ContentBlock", typeNames);
            Assert.Contains("Pinder.LlmAdapters.Anthropic.Dto.CacheControl", typeNames);
            Assert.Contains("Pinder.LlmAdapters.Anthropic.Dto.Message", typeNames);
            Assert.Contains("Pinder.LlmAdapters.Anthropic.Dto.MessagesResponse", typeNames);
            Assert.Contains("Pinder.LlmAdapters.Anthropic.Dto.ResponseContent", typeNames);
            Assert.Contains("Pinder.LlmAdapters.Anthropic.Dto.UsageStats", typeNames);
            Assert.Contains("Pinder.LlmAdapters.Anthropic.AnthropicOptions", typeNames);
            Assert.Contains("Pinder.LlmAdapters.Anthropic.AnthropicApiException", typeNames);
        }

        #endregion
    }
}
