namespace TibidleMultiManager;

public static class TabOrder
{
    public static void Move(List<int> order, int account, int targetAccount)
    {
        var from = order.IndexOf(account);
        var to = order.IndexOf(targetAccount);
        if (from < 0 || to < 0 || from == to) return;
        order.RemoveAt(from);
        order.Insert(to, account);
    }
}
