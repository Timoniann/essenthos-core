namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// What an entity is. Deliberately short: an event is not one of these because an event carries
/// dates and an arithmetic that a person does not, and forcing the two into one table would mean
/// a dozen columns null for every row of one kind.
/// </summary>
public enum EntityKind
{
    Person,
    Place,
}
