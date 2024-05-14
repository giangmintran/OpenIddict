using Microsoft.EntityFrameworkCore;

namespace OpenIdConnect
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions options) : base(options)
        {
        }
    }
}

