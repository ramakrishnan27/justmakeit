using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

/// <summary>Both menus held in memory, so showing a menu reads no database. A menu edit calls <see cref="Invalidate"/>.</summary>
public interface IMenuCache
{
    Task<MenuSnapshot> GetAsync(CancellationToken cancellationToken);

    /// <summary>Forgets what is held, so the next call reads the database.</summary>
    void Invalidate();
}
