using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.Models.Response;

namespace Dhole.AI.Application.Features.Models.GetById;

public sealed record GetModelByIdQuery(Guid Id) : IQuery<Result<AiModelDto>>;
