using First10.Application.Triage;
using First10.Domain.Triage;

namespace First10.Tests;

public class HeuristicIntentClassifierTests
{
    private readonly HeuristicIntentClassifier _classifier = new();

    private async Task<IntentResult> Classify(string text) =>
        await _classifier.ClassifyAsync(text, CancellationToken.None);

    [Theory]
    [InlineData("Accident dey happen for Mowe o! Two okada down", "pidgin")]
    [InlineData("Trailer don jam bus for Kara bridge", "pidgin")]
    [InlineData("There has been a bad accident before the toll gate, people are trapped", "english")]
    [InlineData("Ìjàǹbá ti ṣẹlẹ̀ ní Ibafo", "yoruba")] // diacritics stripped → "ijamba"
    public async Task Incident_reports_classify_as_new_incident(string text, string expectedLanguage)
    {
        var result = await Classify(text);
        Assert.Equal(MessageIntent.NewIncident, result.Intent);
        Assert.Equal(expectedLanguage, result.Language);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("Test")]
    [InlineData("good morning")]
    public async Task Greetings_classify_as_greeting(string text)
    {
        Assert.Equal(MessageIntent.GreetingOrTest, (await Classify(text)).Intent);
    }

    [Fact]
    public async Task Spam_markers_classify_as_spam()
    {
        Assert.Equal(MessageIntent.SpamOrAbuse,
            (await Classify("WIN BIG today!!! play now https://bet.example")).Intent);
    }

    [Fact]
    public async Task Crash_adjacent_words_without_accident_keyword_still_bias_to_incident()
    {
        // D-008 bias rule: "okada" + "blood" + urgency, no explicit "accident".
        var result = await Classify("okada rider dey ground, blood everywhere, abeg send help");
        Assert.Equal(MessageIntent.NewIncident, result.Intent);
    }

    [Fact]
    public async Task Unclassifiable_text_falls_back_to_question()
    {
        Assert.Equal(MessageIntent.Question, (await Classify("which number be this please")).Intent);
    }
}
