using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.AI.Domain.Profiles.Entities;

public sealed class AiProfileModel : Entity<Guid>
{
    private AiProfileModel() { }

    private AiProfileModel(Guid id, Guid profileId, Guid modelId, int priority, bool isFallback)
        : base(id)
    {
        Apply(profileId, modelId, priority, isFallback);
    }

    public Guid ProfileId { get; private set; }

    public Guid ModelId { get; private set; }

    public int Priority { get; private set; }

    public bool IsFallback { get; private set; }

    internal static AiProfileModel Create(
        Guid profileId,
        Guid modelId,
        int priority,
        bool isFallback
    )
    {
        return new AiProfileModel(Guid.NewGuid(), profileId, modelId, priority, isFallback);
    }

    internal void Update(int priority, bool isFallback)
    {
        Apply(ProfileId, ModelId, priority, isFallback);
    }

    private void Apply(Guid profileId, Guid modelId, int priority, bool isFallback)
    {
        if (profileId == Guid.Empty)
        {
            throw new InvalidOperationException("El perfil es obligatorio.");
        }

        if (modelId == Guid.Empty)
        {
            throw new InvalidOperationException("El modelo es obligatorio.");
        }

        if (priority <= 0)
        {
            throw new InvalidOperationException("La prioridad debe ser mayor que cero.");
        }

        ProfileId = profileId;
        ModelId = modelId;
        Priority = priority;
        IsFallback = isFallback;
    }
}
