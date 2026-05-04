using System.Collections.Generic;
using UnityEngine;

public class MeetingPoint : MonoBehaviour
{
    private static readonly List<MeetingPoint> meetingPoints = new List<MeetingPoint>();

    private readonly HashSet<Player> playersInside = new();

    private void OnEnable() => meetingPoints.Add(this);

    private void OnDisable() => meetingPoints.Remove(this);

    private void OnTriggerEnter2D(Collider2D collision)
    {
        var player = collision.GetComponentInParent<Player>();
        if (player != null)
            playersInside.Add(player);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        var player = collision.GetComponentInParent<Player>();
        if (player != null)
            playersInside.Remove(player);
    }

    public static bool IsPlayerInMeetingPoint(Player player)
    {
        foreach (var meetingPoint in meetingPoints)
        {
            if (meetingPoint.playersInside.Contains(player))
                return true;
        }

        return false;
    }
}
