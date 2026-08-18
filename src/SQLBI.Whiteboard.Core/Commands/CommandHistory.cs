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

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

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

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
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
