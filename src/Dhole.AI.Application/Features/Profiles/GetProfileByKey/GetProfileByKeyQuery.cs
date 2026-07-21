using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.Profiles.Response;

namespace Dhole.AI.Application.Features.Profiles.GetByKey;

public sealed record GetProfileByKeyQuery(string Key) : IQuery<Result<AiProfileDto>>;
