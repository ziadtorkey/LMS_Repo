using Lms.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Messaging;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.DirectKey).HasMaxLength(100);
        builder.HasIndex(x => x.DirectKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.LastMessageAtUtc });
    }
}

internal sealed class ConversationParticipantConfiguration : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        builder.ToTable("ConversationParticipants");
        builder.Property(x => x.ParticipantKey).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => x.ParticipantKey).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.ConversationId });
        builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.State).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.ConversationId, x.Id });
        builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
    }
}
