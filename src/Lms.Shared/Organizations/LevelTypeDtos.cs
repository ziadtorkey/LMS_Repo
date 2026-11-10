namespace Lms.Shared.Organizations;

/// <summary>A level type of the client's tree (FR-ORG-02); <c>UnitCount</c> = live units using it.</summary>
public sealed record LevelTypeDto(Guid Id, string NameAr, string NameEn, int Order, bool CanHostClasses, int UnitCount);

/// <summary>Body of POST and PUT /api/level-types.</summary>
public sealed record SaveLevelTypeRequest(string NameAr, string NameEn, bool CanHostClasses);

/// <summary>Body of PUT /api/level-types/order: exactly the client's live level type ids, in the new order.</summary>
public sealed record ReorderLevelTypesRequest(IReadOnlyList<Guid> OrderedIds);
