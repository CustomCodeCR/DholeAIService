using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.Profiles.Response;

namespace Dhole.AI.Application.Features.Profiles.GetById;

public sealed record GetProfileByIdQuery(Guid Id) : IQuery<Result<AiProfileDto>>;
