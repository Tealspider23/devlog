namespace Devlog.Core.Domain;

/// <summary>Counts from one pass of git scanning — what <c>--scan-git</c> and <c>POST /v1/scan-git</c> both report.</summary>
public sealed record GitScanSummary(int Scanned, int Skipped, int ReposFailed);
