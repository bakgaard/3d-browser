using Microsoft.EntityFrameworkCore;
using PatreonBrowser.Api.Models;

namespace PatreonBrowser.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<MiniObject> Objects => Set<MiniObject>();
    public DbSet<Render> Renders => Set<Render>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ObjectCategory> ObjectCategories => Set<ObjectCategory>();
    public DbSet<RenderRegion> RenderRegions => Set<RenderRegion>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Artist>().HasIndex(a => a.Name).IsUnique();

        b.Entity<Release>()
            .HasOne(r => r.Artist).WithMany(a => a.Releases)
            .HasForeignKey(r => r.ArtistId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Release>().HasIndex(r => new { r.ArtistId, r.RelPath }).IsUnique();

        b.Entity<MiniObject>()
            .HasOne(o => o.Release).WithMany(r => r.Objects)
            .HasForeignKey(o => o.ReleaseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MiniObject>().HasIndex(o => o.Key).IsUnique();
        b.Entity<MiniObject>().HasIndex(o => o.Name);
        b.Entity<MiniObject>().HasIndex(o => o.CanonicalObjectId);

        // Primary render: optional, no cascade (clearing handled in app logic on delete).
        b.Entity<MiniObject>()
            .HasOne<Render>().WithMany()
            .HasForeignKey(o => o.PrimaryRenderId)
            .OnDelete(DeleteBehavior.SetNull);

        // Self-reference to the canonical object a merged item points at.
        b.Entity<MiniObject>()
            .HasOne<MiniObject>().WithMany()
            .HasForeignKey(o => o.CanonicalObjectId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<Render>()
            .HasOne(r => r.MiniObject).WithMany(o => o.Renders)
            .HasForeignKey(r => r.MiniObjectId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Render>().HasIndex(r => r.ContainerPath).IsUnique();

        b.Entity<Category>().HasIndex(c => c.NameKey).IsUnique();

        b.Entity<ObjectCategory>().HasKey(oc => new { oc.MiniObjectId, oc.CategoryId });
        b.Entity<ObjectCategory>()
            .HasOne(oc => oc.MiniObject).WithMany(o => o.ObjectCategories)
            .HasForeignKey(oc => oc.MiniObjectId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ObjectCategory>()
            .HasOne(oc => oc.Category).WithMany(c => c.ObjectCategories)
            .HasForeignKey(oc => oc.CategoryId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<RenderRegion>()
            .HasOne(rr => rr.Render).WithMany(r => r.Regions)
            .HasForeignKey(rr => rr.RenderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RenderRegion>()
            .HasOne(rr => rr.Category).WithMany()
            .HasForeignKey(rr => rr.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}
