using ConvenientNote.Infrastructure.Persistence.Entities;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using Microsoft.EntityFrameworkCore;

namespace ConvenientNote.Infrastructure.Persistence;

public sealed class ConvenientNoteDbContext : DbContext
{
    public ConvenientNoteDbContext(DbContextOptions<ConvenientNoteDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkspaceEntity> Workspaces => Set<WorkspaceEntity>();

    public DbSet<NoteEntity> Notes => Set<NoteEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceEntity>(workspace =>
        {
            workspace.ToTable("Workspaces");
            workspace.HasKey(current => current.Id);
            workspace.Property(current => current.Id).ValueGeneratedNever();
            workspace.Property(current => current.Name).HasMaxLength(80).IsRequired();
            workspace.Property(current => current.CreatedAt).IsRequired();
            workspace.Property(current => current.UpdatedAt).IsRequired();

            workspace
                .HasMany(current => current.Notes)
                .WithOne(current => current.Workspace)
                .HasForeignKey(current => current.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NoteEntity>(note =>
        {
            note.ToTable("Notes");
            note.HasKey(current => current.Id);
            note.Property(current => current.Id).ValueGeneratedNever();
            note.Property(current => current.BoardKey).HasMaxLength(64).HasDefaultValue(TodoBoardKeys.DayTodo).IsRequired();
            note.Property(current => current.Priority).HasMaxLength(16).HasDefaultValue(Note.DefaultPriority).IsRequired();
            note.Property(current => current.Title).HasMaxLength(80).IsRequired();
            note.Property(current => current.Content).IsRequired();
            note.Property(current => current.RichContent).IsRequired();
            note.Property(current => current.TagsJson).IsRequired();
            note.Property(current => current.Color).HasMaxLength(32).IsRequired();
            note.Property(current => current.CreatedAt).IsRequired();
            note.Property(current => current.UpdatedAt).IsRequired();

            note.HasIndex(current => current.WorkspaceId);
            note.HasIndex(current => current.BoardKey);
            note.HasIndex(current => current.IsCompleted);
            note.HasIndex(current => current.IsDeleted);
            note.HasIndex(current => current.IsPinned);
            note.HasIndex(current => current.ZIndex);
        });
    }
}
