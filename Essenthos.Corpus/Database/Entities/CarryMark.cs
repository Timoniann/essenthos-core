using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// How far the links have been carried over: the highest link id every name the passes wrote has
/// already crossed.
///
/// <para>
/// A pass carries what it writes along the links that exist when it runs, and a link drawn after
/// it reaches a word no pass that already ran will ever name. Carrying everything again costs
/// minutes on every load whether or not a link moved, so the load carries only over the links
/// newer than this, and moves it on. A link id never comes back once deleted, so a link above it
/// is one nothing has carried over yet.
/// </para>
///
/// <para>
/// One row, because it is one fact about the whole corpus. No row means nothing records how far
/// the carry got, and the next load carries over every link once.
/// </para>
/// </summary>
public class CarryMark
{
    public const int Only = 1;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = Only;

    public long LinkId { get; set; }
}
