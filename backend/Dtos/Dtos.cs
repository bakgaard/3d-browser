namespace PatreonBrowser.Api.Dtos;

public record CategoryDto(int Id, string Name, int ObjectCount);
public record CategoryRefDto(int Id, string Name);

public record AutoTagResultDto(
    int LinksAdded, int CategoriesConsidered, int ObjectsConsidered,
    IReadOnlyList<CategoryTagCountDto> PerCategory);
public record CategoryTagCountDto(string Name, int Added);

public record ArtistDto(int Id, string Name, int ReleaseCount, int ObjectCount);

public record ReleaseDto(
    int Id, int ArtistId, string ArtistName, string Name,
    string? Code, int? Year, int? Month, int ObjectCount);

public record ObjectSummaryDto(
    int Id, string Name,
    int ArtistId, string ArtistName,
    int ReleaseId, string ReleaseName,
    int? ThumbnailRenderId, int RenderCount,
    IReadOnlyList<CategoryRefDto> Categories);
// Name above is the display name (custom title if set, otherwise the scanned name).

public record SearchResultDto(
    int Total, int Page, int PageSize, IReadOnlyList<ObjectSummaryDto> Items);

public record RenderDto(int Id, string FileName, string RelPath);

public record ObjectDetailDto(
    int Id, string Name,
    string? CustomName, string AutoName,
    int ArtistId, string ArtistName,
    int ReleaseId, string ReleaseName,
    string FolderRelPath, string HostPath,
    int ModelFileCount,
    int? PrimaryRenderId,
    IReadOnlyList<CategoryRefDto> Categories,
    IReadOnlyList<RenderDto> Renders);
// Name = display name (CustomName ?? AutoName). AutoName = scanner-derived name.

public record AddCategoryRequest(int? CategoryId, string? Name);
public record CreateCategoryRequest(string Name);
public record SetCustomNameRequest(string? CustomName);
public record SetPrimaryRenderRequest(int RenderId);
public record MergeRequest(IReadOnlyList<int> SourceIds);

public record ScanStatusDto(
    bool Running, DateTime? StartedAt, DateTime? FinishedAt, string? Phase,
    int ArtistsScanned, int ReleasesFound, int ObjectsFound, int RendersFound,
    int ObjectsAdded, int RendersAdded, IReadOnlyList<string> Warnings, string? Message);
