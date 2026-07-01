using System;
using UnityEngine;

// The ONE entry point for every real-money purchase (Legendary Pack, Coins tab, Gems tab).
// Per project rule: mobile payments are Apple/Google in-app billing only — this bridge is where
// Unity IAP plugs in later. Until the Unity IAP package + store console products exist, it's a
// stub that "succeeds" immediately so the buttons are fully wired in-game.
//
// TODO(IAP): replace the body of PurchaseProduct with real Unity IAP (initialize the store,
// map productId → store product, call onSuccess only from the purchase callback). Nothing else
// in the codebase should need to change — callers already treat this as async-capable.
public static class IAPBridge
{
    public static void PurchaseProduct(string productId, Action onSuccess)
    {
        Debug.Log("[IAP STUB] would purchase " + productId + " via Apple/Google billing — granting immediately.");
        onSuccess?.Invoke();
    }
}
