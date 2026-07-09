/*
 * Ch.5 incremental taxonomy helpers (Fig 5.5).
 */

namespace UKS;

public partial class UKS
{
    /// <summary>
    /// Insert <paramref name="insertedCategory"/> between <paramref name="childCategory"/> and
    /// <paramref name="parentCategory"/> without rewriting instances.
    /// </summary>
    public void InsertCategoryBetween(Thought childCategory, Thought insertedCategory, Thought parentCategory)
    {
        if (childCategory is null || insertedCategory is null || parentCategory is null) return;
        Thought? isA = Labeled("is-a");
        if (isA is null) return;

        childCategory.RemoveLink(isA, parentCategory);
        AddStatement(childCategory, isA, insertedCategory);
        AddStatement(insertedCategory, isA, parentCategory);
    }

    /// <summary>Ch.5 explainability trace: [querySource → category → attribute].</summary>
    public List<Thought> ExplainLink(Link link, Thought querySource) =>
        ExplainInheritance(link, querySource);

    /// <summary>Ch.5 explainability trace: [querySource → category → attribute].</summary>
    public List<Thought> ExplainInheritance(Link link, Thought querySource)
    {
        var trace = new List<Thought> { querySource };
        if (link.InheritedFromCategory is not null)
            trace.Add(link.InheritedFromCategory);
        if (link.To is not null)
            trace.Add(link.To);
        return trace;
    }
}