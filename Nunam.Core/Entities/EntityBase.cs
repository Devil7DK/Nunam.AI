using System;
using System.Collections.Generic;
using System.Text;

namespace Nunam.Core.Entities
{
    public abstract class EntityBase
    {
        public Guid Id { get; private init; } = Guid.NewGuid();
        public DateTimeOffset Created { get; private set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastModified { get; private set; } = DateTimeOffset.UtcNow;
    }
}
