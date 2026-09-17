namespace Essenthos.Core.Bhsa.Attributes;

/// <summary>
/// Types for text objects. As text objects are represented by nodes in Text-Fabric, we shall use both object and node without much consistency.
/// </summary>
public class NodeType : StringEnum<NodeType>
{
    public static readonly NodeType Word = new("word");
    public static readonly NodeType Lexeme = new("lex");
    public static readonly NodeType Subphrase = new("subphrase");
    public static readonly NodeType Phrase = new("phrase");
    public static readonly NodeType PhraseAtom = new("phrase_atom");
    public static readonly NodeType Clause = new("clause");
    public static readonly NodeType ClauseAtom = new("clause_atom");
    public static readonly NodeType Sentence = new("sentence");
    public static readonly NodeType SentenceAtom = new("sentence_atom");
    public static readonly NodeType HalfVerse = new("half_verse");
    public static readonly NodeType Verse = new("verse");
    public static readonly NodeType Chapter = new("chapter");
    public static readonly NodeType Book = new("book");

    protected NodeType(string value, bool external = false) : base(value, external)
    {
    }
}