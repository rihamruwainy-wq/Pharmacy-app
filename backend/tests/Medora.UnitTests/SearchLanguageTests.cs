using Medora.Application.Features.Catalog;

namespace Medora.UnitTests;

public class SearchLanguageTests
{
    [Theory]
    [InlineData("Tylenol", "paracetamol")]
    [InlineData("acetaminophen", "paracetamol")]
    [InlineData("باراسيتامول", "paracetamol")]
    [InlineData("بروفين", "ibuprofen")]
    [InlineData("Ventolin", "salbutamol")]
    public void Brands_and_arabic_names_expand_to_the_generic(string query, string expected)
    {
        var plan = SearchLanguage.Plan(query);
        Assert.Contains(expected, Assert.Single(plan.TermGroups));
    }

    [Theory]
    [InlineData("headache", "pain-relief")]
    [InlineData("صداع", "pain-relief")]
    [InlineData("heartburn", "digestive")]
    [InlineData("حموضة", "digestive")]
    [InlineData("كحة", "cold-flu")]
    [InlineData("blood pressure", "heart")]
    [InlineData("ضغط الدم", "heart")]
    [InlineData("Itchy skin", "skin")]
    [InlineData("Baby care", "baby")]
    [InlineData("العناية بالطفل", "baby")]
    [InlineData("حكة الجلد", "skin")]
    public void Symptoms_map_to_categories_instead_of_text_matching(string query, string category)
    {
        var plan = SearchLanguage.Plan(query);
        Assert.Contains(category, plan.Categories);
        Assert.Empty(plan.TermGroups);
    }

    [Fact]
    public void Arabic_letter_variants_and_diacritics_are_unified() =>
        Assert.Equal(SearchLanguage.Normalize("اسبرين"), SearchLanguage.Normalize("أَسْبِرين"));

    [Fact]
    public void Stop_words_are_ignored_and_every_remaining_word_must_match()
    {
        var plan = SearchLanguage.Plan("medicine for the vitamin d");
        Assert.Single(plan.TermGroups); // "d" too short, "medicine/for/the" are stop words
    }

    [Theory]
    [InlineData("ibuprophen", "ibuprofen")]
    [InlineData("ibuporfen", "ibuprofen")]   // transposition counts as one edit
    [InlineData("cetrizine", "cetirizine")]
    public void Typos_are_corrected_against_the_vocabulary(string typo, string expected) =>
        Assert.Equal(expected, SearchLanguage.Correct(typo, ["ibuprofen", "cetirizine", "paracetamol"]));

    [Fact]
    public void Known_words_are_not_corrected() =>
        Assert.Null(SearchLanguage.Correct("paracetamol", ["paracetamol"]));
}
