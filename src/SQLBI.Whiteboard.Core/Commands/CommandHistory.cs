using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Commands;

public interface IBoardCommand
{
    void Execute(BoardDocument document);
    void Undo(BoardDocument document);
}

public sealed class CommandHistory
{
    private readonly Stack<IBoardCommand> _undo = [];
    private readonly Stack<IBoardCommand> _redo = [];
    private IBoardCommand? _savePoint;

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Whether the document stands where it stood when <see cref="MarkSaved"/> was last
    /// called - which is what makes a board that has been undone back to its saved state
    /// count as unmodified, and a board that has been undone <em>past</em> it count as
    /// modified again.
    /// </summary>
    /// <remarks>
    /// The mark is the command on top of the undo stack rather than the stack's depth: an
    /// undo followed by a different action returns to the same depth by a different route,
    /// and only the identity of the command tells the two apart. Reference equality is
    /// deliberate, since commands are records and two structurally identical ones are still
    /// two separate steps.
    /// </remarks>
    public bool IsAtSavePoint => ReferenceEquals(Top, _savePoint);

    private IBoardCommand? Top => _undo.Count > 0 ? _undo.Peek() : null;

    /// <summary>
    /// Records that the document as it stands now is what the file on disk contains.
    /// </summary>
    public void MarkSaved()
    {
        _savePoint = Top;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Execute(IBoardCommand command, BoardDocument document)
    {
        command.Execute(document);
        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RecordExecuted(IBoardCommand command)
    {
        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo(BoardDocument document)
    {
        if (!_undo.TryPop(out var command))
        {
            return;
        }

        command.Undo(document);
        _redo.Push(command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo(BoardDocument document)
    {
        if (!_redo.TryPop(out var command))
        {
            return;
        }

        command.Execute(document);
        _undo.Push(command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drops both stacks and puts the save point at the empty history, so a board that has
    /// just been opened or created counts as unmodified. A caller that clears the history
    /// around content the file on disk does not contain - an import opened as a new board -
    /// has to say so itself.
    /// </summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savePoint = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record AddObjectCommand(BoardObject Item) : IBoardCommand
{
    public void Execute(BoardDocument document) => document.AddObject(Item);
    public void Undo(BoardDocument document) => document.RemoveObject(Item.Id);
}

public sealed record AddImportCommand(
    IReadOnlyList<BoardObject> Objects,
    IReadOnlyList<BoardAsset> Assets) : IBoardCommand
{
    public void Execute(BoardDocument document)
    {
        foreach (var asset in Assets)
        {
            document.AddAsset(asset);
        }

        foreach (var item in Objects)
        {
            document.AddObject(item);
        }
    }

    public void Undo(BoardDocument document)
    {
        foreach (var item in Objects)
        {
            document.RemoveObject(item.Id);
        }

        foreach (var asset in Assets)
        {
            document.RemoveAsset(asset.Id);
        }
    }
}

public sealed record RemoveObjectsCommand(IReadOnlyList<BoardObject> Items) : IBoardCommand
{
    public void Execute(BoardDocument document)
    {
        foreach (var item in Items)
        {
            document.RemoveObject(item.Id);
        }
    }

    public void Undo(BoardDocument document)
    {
        foreach (var item in Items)
        {
            document.AddObject(item);
        }
    }
}

public sealed record ReplaceObjectCommand(BoardObject Before, BoardObject After) : IBoardCommand
{
    public void Execute(BoardDocument document) => document.ReplaceObject(After);
    public void Undo(BoardDocument document) => document.ReplaceObject(Before);
}

public sealed record ReplaceObjectsCommand(
    IReadOnlyList<BoardObject> Before,
    IReadOnlyList<BoardObject> After) : IBoardCommand
{
    public void Execute(BoardDocument document) => document.ReplaceObjects(After);
    public void Undo(BoardDocument document) => document.ReplaceObjects(Before);
}
