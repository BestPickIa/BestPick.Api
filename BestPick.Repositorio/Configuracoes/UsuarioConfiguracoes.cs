using BestPick.Dominio.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BestPick.Repositorio.Configuracoes;

public class UsuarioConfiguracoes : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("Usuario").HasKey(x => x.Id);

        builder.Property(nameof(Usuario.Id)).HasColumnName("UsuarioId");
        builder.Property(nameof(Usuario.Nome)).HasColumnName("Nome").HasMaxLength(150);
        builder.Property(nameof(Usuario.Email)).HasColumnName("Email").HasMaxLength(254);
        builder.Property(nameof(Usuario.Ativo)).HasColumnName("Ativo").IsConcurrencyToken();
        builder.Property(nameof(Usuario.Senha)).HasColumnName("Senha").HasMaxLength(255).IsConcurrencyToken();
        builder.Property(x => x.FotoNomeArquivo).HasMaxLength(36).IsRequired(false).IsConcurrencyToken();
        builder.Property(x => x.FotoAtualizadaEm).IsRequired(false);

    }
}
