using SentimentAI.Core.Privacy;
using SentimentAI.Core.Providers;
using SentimentAI.Core.Errors;

namespace SentimentAI.Core.Models;

/// <summary>Analiz sonucu ve nasıl elde edildiğine dair bilgiler.</summary>
public sealed record AnalysisOutcome(
    AnalysisResult Result,
    AnonymizationReport Anonymization,
    bool UsedFallback);

/// <summary>Ana sağlayıcı başarısız olduğunda kullanıcıdan alınacak onayın içeriği.</summary>
public sealed record FallbackRequest(IAIProvider From, IAIProvider To, ErrorKind Reason);
