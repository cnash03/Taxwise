namespace TaxWise.Rag.Answering;

/// <summary>"Anthropic" config section (BUILD_PLAN §3). The key is <c>ANTHROPIC_API_KEY</c>, never in appsettings.</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>low | medium | high | xhigh | max. Thinking is always on for this model; effort is the only control.</summary>
    public string Effort { get; set; } = "medium";
    public int MaxTokens { get; set; } = 16000;
    /// <summary>Server-side refusal fallback (beta). Turn off if the account doesn't have the beta.</summary>
    public bool UseRefusalFallback { get; set; } = true;
}

/// <summary>"Retrieval" config section (BUILD_PLAN §3).</summary>
public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    public int VectorTopK { get; set; } = 8;
    public int RerankTopK { get; set; } = 4;
    /// <summary>Starting guess; calibrated on the gold set in Phase 7.</summary>
    public double MinRerankScore { get; set; } = 0.35;
}
