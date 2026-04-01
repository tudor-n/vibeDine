# Raport de Testare API: Flux Comandă Client

**Data:** 1 Aprilie 2026  
**Fișier testat:** `CustomerController.cs`  
**Status General:** 🔴 Blocat (Bug de persistență date)  

## 1. Sumar Executiv
Am rulat testele de integrare pentru fluxul principal de comandă al clientului (scanare QR -> vizualizare meniu -> adăugare produse -> trimitere comandă). Toate endpoint-urile răspund corect la nivel de request/response, dar am identificat un **bug critic (fals pozitiv)** la pasul de adăugare a produselor în coș, care blochează finalizarea comenzii.

---

## 2. Status Endpoint-uri

### 🟢 1. `GET /api/tables/{qrToken:guid}/session`
* **Status:** Pass
* **Observații:** Validarea pe format `guid` la nivel de rută funcționează corect. Respinge token-urile invalide (404) și returnează datele corecte pentru cele valide (200 OK). ID-ul comenzii (`activeOrder.id`) a fost extras cu succes.

### 🟢 2. `GET /api/menu`
* **Status:** Pass
* **Observații:** Returnează cu succes produsele disponibile. Am extras un `productId` valid pentru testele următoare.

### 🔴 3. `PATCH /api/orders/{orderId}/items` (Add Items)
* **Status:** Fals Pozitiv (Bug de salvare în baza de date)
* **Observații:** API-ul returnează `200 OK`, un mesaj de succes și un `totalAmount` calculat corect în răspunsul JSON. **Totuși, noile produse NU sunt salvate în baza de date.** Entity Framework actualizează `TotalAmount` pe comanda existentă, dar ignoră complet inserarea produselor în tabelul `OrderItems`.

### 🔴 4. `PATCH /api/orders/{orderId}/submit` (Submit Order)
* **Status:** Blocat (din cauza Pasului 3)
* **Observații:** Din cauză că Pasul 3 nu salvează efectiv produsele, acest endpoint citește din DB o comandă cu 0 produse. Conform logicii din cod, returnează corect `400 Bad Request: "Cannot submit an empty order. Please add items to your cart first."`

---

## 3. Detalii Tehnice & Root Cause (Pentru Backend)

Problema de la Pasul 3 se află în metoda `AddItemsOrder`. Când se creează un produs nou pe ramura `else`, obiectul este adăugat doar în lista din memorie asociată comenzii, dar nu este marcat pentru a fi urmărit (tracked) de `DbContext` în vederea inserării (INSERT) în tabelul fizic.

**Snippet-ul cu problema:**
```csharp
else
{
    // Produsul este creat și adăugat în memorie
    order.Items.Add(new OrderItem
    {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        ProductId = product.Id,
        // ...
    });
    // BUG: Lipsește comanda prin care EF Core este instruit să adauge efectiv rândul în DB la SaveChangesAsync().
}