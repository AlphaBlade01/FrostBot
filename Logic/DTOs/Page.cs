namespace FrostBot.Logic.DTOs;

public readonly record struct Page<T>(
    IReadOnlyCollection<T> Items,
    bool HasNextPage
);
