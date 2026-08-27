using System.Linq;
using System.IO;
using Xunit;

namespace UKS.Tests;

public class UKSConditionalTests
{
    [Fact]
    public void Fido_demo_exposes_true_and_false_conditional_results()
    {
        // English intent: the shipped demo says Fido is outside and that it is raining.
        // Therefore the wet and alert rules are true, while the explicitly-not-raining dry rule is false.
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        string demoPath = Path.Combine(FindRepositoryRoot(),
            "BrainSimulator", "UKSContent", "ch4ch5-fidodemo.txt");

        uks.ImportTextFile(demoPath);

        Thought fido = uks.GetOrAddThought("Fido")!;
        var attributes = uks.GetAttributes(fido);
        Assert.Contains(attributes, IsWet);
        Assert.Contains(attributes, link => link.LinkType?.Label == "is" && link.To?.Label == "alert");
        Assert.DoesNotContain(attributes, IsDry);
    }

    [Fact]
    public void Question_mark_component_creates_a_conditional_link_type()
    {
        // English intent: "play.?" means the semantic relationship "play" used as a clause,
        // not as an assertion. It therefore inherits from both "play" and the conditional "?" category.
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();

        Thought conditionalPlay = uks.GetOrAddThought("play.?")!;

        Assert.Contains(conditionalPlay.Parents, parent => parent.Label == "play");
        Assert.Contains(conditionalPlay.Parents, parent => parent.Label == "?");
        Assert.True(conditionalPlay.HasProperty("isConditional"));
        Assert.True(UKS.IsConditionalLinkType(conditionalPlay));
        Assert.DoesNotContain(conditionalPlay.GetAttributes(), attribute => attribute.Label == "?");
    }

    [Fact]
    public void Successful_conditional_is_returned_as_an_assertion_without_changing_the_rule()
    {
        // English intent: after the rule proves that Fido is wet, callers receive "Fido is wet."
        // The stored result clause remains "Fido is.? wet" so the rule itself is not rewritten as an assertion.
        var (uks, fido, _, isType, wet, outside, _) = CreateWeatherFixture();
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;
        var outsideCondition = new Link(fido, conditionalIs, outside);
        Link storedResultClause = uks.AddStatement(fido, conditionalIs, wet)!;
        var fullStatement = uks.AddStatement(storedResultClause, "IF", outsideCondition);
        uks.AddStatement(fido, isType, outside);

        Link returnedResult = Assert.Single(uks.GetAttributes(fido),
            link => link.To == wet);

        Assert.Equal("is", returnedResult.LinkType?.Label);
        Assert.Equal("is.?", storedResultClause.LinkType?.Label);
    }

    [Fact]
    public void Play_result_is_returned_only_when_weather_condition_is_met()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        Thought fido = uks.GetOrAddThought("Fido")!;
        Thought weather = uks.GetOrAddThought("weather")!;
        Thought outside = uks.GetOrAddThought("outside")!;
        Thought raining = uks.GetOrAddThought("raining")!;
        Thought playConditional = uks.GetOrAddThought("play.?")!;
        Thought isConditional = uks.GetOrAddThought("is.?")!;
        Thought isType = uks.GetOrAddThought("is", "LinkType")!;
        var weatherCondition = new Link(weather, isConditional, raining);
        Link playResult = uks.AddStatement(fido, playConditional, outside)!;
        uks.AddStatement(playResult, "IF", weatherCondition);

        Assert.DoesNotContain(uks.GetAttributes(fido),
            link => link.To == outside);

        uks.AddStatement(weather, isType, raining);

        Assert.Contains(uks.GetAttributes(fido),
            link => link.LinkType?.Label == "play" && link.To == outside);
        Assert.DoesNotContain(uks.GetAttributes(fido),
            link => link.LinkType?.Label == "play.?" && link.To == outside);
    }

    [Fact]
    public void Question_mark_label_without_property_is_not_conditional()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        Thought linkType = uks.GetOrAddThought("play.?")!;

        linkType.RemoveParent("?");

        Assert.False(linkType.HasProperty("isConditional"));
        Assert.False(UKS.IsConditionalLinkType(linkType));
    }

    [Fact]
    public void And_condition_returns_result_only_when_both_facts_are_present()
    {
        // English intent: "Fido is wet if Fido is outside and the weather is raining."
        // Being outside by itself is not enough; once it is also raining, Fido is wet.
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();

        Thought fido = uks.GetOrAddThought("Fido")!;
        Thought weather = uks.GetOrAddThought("weather")!;
        Thought isType = uks.GetOrAddThought("is", "LinkType")!;
        Thought wet = uks.GetOrAddThought("wet")!;
        Thought outside = uks.GetOrAddThought("outside")!;
        Thought raining = uks.GetOrAddThought("raining")!;
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;

        var outsideCondition = new Link(fido, conditionalIs, outside);
        var rainingCondition = new Link(weather, conditionalIs, raining);
        Link andCondition = uks.AddStatement(
            outsideCondition,
            "AND",
            rainingCondition)!;

        Link wetResult = uks.AddStatement(fido, conditionalIs, wet)!;
        var fullStatement = uks.AddStatement(wetResult, "IF", andCondition);

        uks.AddStatement(fido, isType, outside);
        Assert.DoesNotContain(uks.GetAttributes(fido), IsWet);

        uks.AddStatement(weather, isType, raining);
        Assert.Contains(uks.GetAttributes(fido), IsWet);

        static bool IsWet(Link link) => link.LinkType?.Label == "is" && link.To?.Label == "wet";
    }

    [Fact]
    public void And_condition_is_not_met_when_only_the_second_fact_is_present()
    {
        // English intent: "Fido is wet if Fido is outside and the weather is raining."
        // Rain by itself is not enough when there is no fact saying that Fido is outside.
        var (uks, fido, weather, isType, wet, outside, raining) = CreateWeatherFixture();
        AddWetWhenOutsideAndRainingRule(uks, fido, weather, isType, wet, outside, raining);

        uks.AddStatement(weather, isType, raining);

        Assert.DoesNotContain(uks.GetAttributes(fido), IsWet);
    }

    [Fact]
    public void A_single_condition_does_not_require_an_and_relationship()
    {
        // English intent: "Fido is wet if Fido is outside."
        // This verifies that a simple IF condition still works without manufacturing an AND expression.
        var (uks, fido, _, isType, wet, outside, _) = CreateWeatherFixture();
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;
        var outsideCondition = new Link(fido, conditionalIs, outside);
        Link wetResult = uks.AddStatement(fido, conditionalIs, wet)!;
        var fullStatement = uks.AddStatement(wetResult, "IF", outsideCondition);

        Assert.DoesNotContain(uks.GetAttributes(fido), IsWet);

        uks.AddStatement(fido, isType, outside);

        Assert.Contains(uks.GetAttributes(fido), IsWet);
    }

    [Fact]
    public void Nested_and_conditions_require_every_fact()
    {
        // English intent: "Fido is wet if Fido is outside, the weather is raining, and Fido is awake."
        // The first two facts are not enough; the result becomes true only after the third fact is present.
        var (uks, fido, weather, isType, wet, outside, raining) = CreateWeatherFixture();
        Thought awake = uks.GetOrAddThought("awake")!;
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;
        var outsideCondition = new Link(fido, conditionalIs, outside);
        var rainingCondition = new Link(weather, conditionalIs, raining);
        var awakeCondition = new Link(fido, conditionalIs, awake);
        Link firstTwoConditions = uks.AddStatement(outsideCondition, "AND", rainingCondition)!;
        Link allThreeConditions = uks.AddStatement(firstTwoConditions, "AND", awakeCondition)!;
        Link wetResult = uks.AddStatement(fido, conditionalIs, wet)!;
        var fullStatement = uks.AddStatement(wetResult, "IF", allThreeConditions);

        uks.AddStatement(fido, isType, outside);
        uks.AddStatement(weather, isType, raining);
        Assert.DoesNotContain(uks.GetAttributes(fido), IsWet);

        uks.AddStatement(fido, isType, awake);

        Assert.Contains(uks.GetAttributes(fido), IsWet);
    }

    [Fact]
    public void And_is_an_ordinary_relationship_when_it_is_not_a_condition()
    {
        // English intent: none is assigned here. "Suzy AND Mary" is deliberately treated as stored data,
        // not as a logical expression, because it is not being evaluated as the condition of an IF rule.
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        Thought suzy = uks.GetOrAddThought("Suzy")!;
        Thought mary = uks.GetOrAddThought("Mary")!;

        uks.AddStatement(suzy, "AND", mary);

        Assert.Contains(uks.GetAttributes(suzy),
            link => link.LinkType?.Label == "AND" && link.To == mary);
    }

    [Fact]
    public void Explicit_negative_condition_is_not_met_when_no_negative_fact_is_present()
    {
        // English intent: "Fido is dry if the weather is not raining."
        // The absence of rain is not evidence that it is not raining. Without an explicit
        // [weather -> is.not -> raining] assertion, the rule must not make Fido dry.
        var (uks, fido, weather, isType, _, _, raining) = CreateWeatherFixture();
        Thought dry = uks.GetOrAddThought("dry")!;
        Thought isNotType = uks.GetOrAddThought("is.not", "LinkType")!;
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;
        Thought conditionalIsNot = uks.GetOrAddThought("is.not.?")!;
        AddDryWhenExplicitlyNotRainingRule(uks, fido, weather, conditionalIs, conditionalIsNot, dry, raining);

        Assert.DoesNotContain(uks.GetAttributes(fido), IsDry);
    }

    [Fact]
    public void Explicit_negative_condition_is_met_when_the_negative_fact_is_present()
    {
        // English intent: "Fido is dry if the weather is not raining."
        // The explicit [weather -> is.not -> raining] assertion satisfies the condition.
        var (uks, fido, weather, isType, _, _, raining) = CreateWeatherFixture();
        Thought dry = uks.GetOrAddThought("dry")!;
        Thought isNotType = uks.GetOrAddThought("is.not", "LinkType")!;
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;
        Thought conditionalIsNot = uks.GetOrAddThought("is.not.?")!;
        AddDryWhenExplicitlyNotRainingRule(uks, fido, weather, conditionalIs, conditionalIsNot, dry, raining);
        uks.AddStatement(weather, isNotType, raining);

        Assert.Contains(uks.GetAttributes(fido), IsDry);
    }

    [Fact]
    public void Positive_fact_does_not_satisfy_an_explicit_negative_condition()
    {
        // English intent: "Fido is dry if the weather is not raining."
        // [weather -> is -> raining] is the opposite assertion, so it cannot satisfy is.not.
        var (uks, fido, weather, isType, _, _, raining) = CreateWeatherFixture();
        Thought dry = uks.GetOrAddThought("dry")!;
        Thought isNotType = uks.GetOrAddThought("is.not", "LinkType")!;
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;
        Thought conditionalIsNot = uks.GetOrAddThought("is.not.?")!;
        AddDryWhenExplicitlyNotRainingRule(uks, fido, weather, conditionalIs, conditionalIsNot, dry, raining);
        uks.AddStatement(weather, isType, raining);

        Assert.DoesNotContain(uks.GetAttributes(fido), IsDry);
    }

    [Fact]
    public void Explicit_negative_fact_invalidates_the_corresponding_positive_condition()
    {
        // English intent: "Fido is alert if Fido is outside."
        // After the assertion changes to "Fido is not outside," the positive outside condition is false,
        // so the full Fido attribute list must no longer contain "Fido is alert."
        var (uks, fido, _, isType, _, outside, _) = CreateWeatherFixture();
        Thought alert = uks.GetOrAddThought("alert")!;
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;
        Thought isNotType = uks.GetOrAddThought("is.not")!;
        var outsideCondition = new Link(fido, conditionalIs, outside);
        Link alertResult = uks.AddStatement(fido, conditionalIs, alert)!;
        var fullStatement = uks.AddStatement(alertResult, "IF", outsideCondition);
        uks.AddStatement(fido, isType, outside);
        Assert.Contains(uks.GetAttributes(fido), IsAlert);

        uks.AddStatement(fido, isNotType, outside);

        Assert.DoesNotContain(uks.GetAttributes(fido), IsAlert);
    }

    [Fact]
    public void And_with_explicit_negative_condition_requires_both_assertions()
    {
        // English intent: "Fido is comfortable if Fido is outside and the weather is explicitly not raining."
        // Outside alone is insufficient. The result appears after the negative rain assertion is added.
        var (uks, fido, weather, isType, _, outside, raining) = CreateWeatherFixture();
        Thought comfortable = uks.GetOrAddThought("comfortable")!;
        Thought isNotType = uks.GetOrAddThought("is.not", "LinkType")!;
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;
        Thought conditionalIsNot = uks.GetOrAddThought("is.not.?")!;
        var outsideCondition = new Link(fido, conditionalIs, outside);
        var notRainingCondition = new Link(weather, conditionalIsNot, raining);
        Link outsideAndNotRaining = uks.AddStatement(outsideCondition, "AND", notRainingCondition)!;
        Link comfortableResult = uks.AddStatement(fido, conditionalIs, comfortable)!;
        var fullStatement = uks.AddStatement(comfortableResult, "IF", outsideAndNotRaining);
        uks.AddStatement(fido, isType, outside);

        Assert.DoesNotContain(uks.GetAttributes(fido), IsComfortable);

        uks.AddStatement(weather, isNotType, raining);

        Assert.Contains(uks.GetAttributes(fido), IsComfortable);
    }

    private static (UKS uks, Thought fido, Thought weather, Thought isType,
        Thought wet, Thought outside, Thought raining) CreateWeatherFixture()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        return (
            uks,
            uks.GetOrAddThought("Fido")!,
            uks.GetOrAddThought("weather")!,
            uks.GetOrAddThought("is", "LinkType")!,
            uks.GetOrAddThought("wet")!,
            uks.GetOrAddThought("outside")!,
            uks.GetOrAddThought("raining")!);
    }

    private static void AddWetWhenOutsideAndRainingRule(
        UKS uks, Thought fido, Thought weather, Thought isType,
        Thought wet, Thought outside, Thought raining)
    {
        var outsideCondition = new Link(fido, isType, outside);
        Thought conditionalIs = uks.GetOrAddThought("is.?")!;
        outsideCondition.LinkType = conditionalIs;
        var rainingCondition = new Link(weather, conditionalIs, raining);
        Link andCondition = uks.AddStatement(outsideCondition, "AND", rainingCondition)!;
        Link wetResult = uks.AddStatement(fido, conditionalIs, wet)!;
        var fullStatement = uks.AddStatement(wetResult, "IF", andCondition);
    }

    private static void AddDryWhenExplicitlyNotRainingRule(
        UKS uks, Thought fido, Thought weather, Thought conditionalIs, Thought conditionalIsNot,
        Thought dry, Thought raining)
    {
        var notRainingCondition = new Link(weather, conditionalIsNot, raining);
        Link dryResult = uks.AddStatement(fido, conditionalIs, dry)!;
        var fullStatement = uks.AddStatement(dryResult, "IF", notRainingCondition);
    }

    private static bool IsWet(Link link) =>
        link.LinkType?.Label == "is" && link.To?.Label == "wet";

    private static bool IsDry(Link link) =>
        link.LinkType?.Label == "is" && link.To?.Label == "dry";

    private static bool IsComfortable(Link link) =>
        link.LinkType?.Label == "is" && link.To?.Label == "comfortable";

    private static bool IsAlert(Link link) =>
        link.LinkType?.Label == "is" && link.To?.Label == "alert";

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("BrainSim Thought repository root not found.");
    }
}
