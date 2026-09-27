using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResidentialAmenities.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260927163000_InitialInfrastructure")]
public partial class InitialInfrastructure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.12")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
    }
}
