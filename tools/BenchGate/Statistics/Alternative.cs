namespace BenchGate.Statistics;

/// <summary>
/// Specifies the alternative (research) hypothesis for a statistical test such as the
/// <see cref="MannWhitney"/> U test.
/// </summary>
/// <remarks>
/// The alternative hypothesis determines which tail(s) of the reference distribution
/// are used to compute the p-value.
/// <list type="bullet">
/// <item><see cref="TwoSided"/>: difference in distributions in either direction.</item>
/// <item><see cref="Greater"/>: first sample tends to have larger values (shift to the right).</item>
/// <item><see cref="Less"/>: first sample tends to have smaller values (shift to the left).</item>
/// </list>
/// In this library the first sample is the <c>x</c> parameter passed to test methods and the
/// second sample is the <c>y</c> parameter. For example, <see cref="Alternative.Greater"/>
/// tests H1: median(x) &gt; median(y) (or more generally that the distribution of <c>x</c>
/// is stochastically larger than that of <c>y</c>).
/// </remarks>
public enum Alternative
{
    /// <summary>
    /// Two?sided alternative: the distributions differ (shift in either direction).
    /// The reported p-value is 2 * min(Pr(U &lt;= observed), Pr(U &gt;= observed)).
    /// </summary>
    TwoSided,

    /// <summary>
    /// One?sided alternative: the first sample tends to produce larger observations than the second.
    /// Interpreted as H1: distribution(x) is stochastically greater than distribution(y).
    /// </summary>
    Greater,

    /// <summary>
    /// One?sided alternative: the first sample tends to produce smaller observations than the second.
    /// Interpreted as H1: distribution(x) is stochastically less than distribution(y).
    /// </summary>
    Less
}