using System;
using System.Collections.Generic;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// The result returned by <see cref="HarnessRunner.RunAsync"/>.
    ///
    /// <para>
    /// <b>Back-compat:</b> <see cref="Transcript"/> is byte-identical to what
    /// <c>RunAsync</c> returned before this type was introduced (previously the
    /// method returned <c>Task&lt;string&gt;</c>). Callers that only need the
    /// markdown output access <c>result.Transcript</c>; callers that want the
    /// structured LLM data access <c>result.RawSessions</c>.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="RawSessions"/>:</b> populated only when the
    /// <see cref="HarnessRunner"/> was constructed with a transport that is (or is
    /// wrapped in) a <see cref="RecordingLlmTransport"/>. When no recorder is
    /// present the list is empty — this preserves the previous behaviour
    /// transparently.
    /// </para>
    ///
    /// <para>
    /// netstandard2.0 / LangVersion 8.0 — intentionally a normal sealed class
    /// (not a C# 9 <c>record</c>) for language-version compatibility.
    /// </para>
    /// </summary>
    public sealed class HarnessRunResult
    {
        /// <summary>
        /// The full annotated markdown transcript — identical to the string
        /// previously returned by <c>RunAsync()</c>.
        /// </summary>
        public string Transcript { get; }

        /// <summary>
        /// Structured list of every raw LLM exchange captured during the run.
        /// Empty when no <see cref="RecordingLlmTransport"/> was used.
        /// </summary>
        public IReadOnlyList<RawLlmSession> RawSessions { get; }

        public HarnessRunResult(string transcript, IReadOnlyList<RawLlmSession> rawSessions)
        {
            Transcript  = transcript  ?? throw new ArgumentNullException(nameof(transcript));
            RawSessions = rawSessions ?? throw new ArgumentNullException(nameof(rawSessions));
        }
    }
}
