using System;
using UnityEngine;

public static class CommonWindowEvents
{
    public static event Action<MonoBehaviour> WindowOpened;

    public static void NotifyWindowOpened(MonoBehaviour openedWindow)
    {
        WindowOpened?.Invoke(openedWindow);
    }
}
