using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.Connections.Response;

namespace Dhole.AI.Application.Features.Connections.GetById;

public sealed record GetConnectionByIdQuery(Guid Id) : IQuery<Result<AiConnectionDto>>;
