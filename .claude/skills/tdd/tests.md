# Good and Bad Tests

## Good Tests

**Integration-style**: Test through real interfaces, not mocks of internal parts.

```typescript
// GOOD: Tests observable behavior
test("user can checkout with valid cart", async () => {
  const cart = createCart();
  cart.add(product);
  const result = await checkout(cart, paymentMethod);
  expect(result.status).toBe("confirmed");
});
```

Characteristics:

- Tests behavior users/callers care about
- Uses public API only
- Survives internal refactors
- Describes WHAT, not HOW
- One logical assertion per test

## Bad Tests

**Implementation-detail tests**: Coupled to internal structure.

```typescript
// BAD: Tests implementation details
test("checkout calls paymentService.process", async () => {
  const mockPayment = jest.mock(paymentService);
  await checkout(cart, payment);
  expect(mockPayment.process).toHaveBeenCalledWith(cart.total);
});
```

Red flags:

- Mocking internal collaborators
- Testing private methods
- Asserting on call counts/order
- Test breaks when refactoring without behavior change
- Test name describes HOW not WHAT
- Verifying through external means instead of interface
- Expected value computed the way the code computes it

```typescript
// BAD: Bypasses interface to verify
test("createUser saves to database", async () => {
  await createUser({ name: "Alice" });
  const row = await db.query("SELECT * FROM users WHERE name = ?", ["Alice"]);
  expect(row).toBeDefined();
});

// GOOD: Verifies through interface
test("createUser makes user retrievable", async () => {
  const user = await createUser({ name: "Alice" });
  const retrieved = await getUser(user.id);
  expect(retrieved.name).toBe("Alice");
});
```

**Tautological tests**: The expected value restates the implementation, so the test
passes by construction and can never disagree with the code.

```typescript
// BAD: Expected value is recomputed the way the code computes it
test("calculateTotal sums line items", () => {
  const items = [{ price: 10 }, { price: 5 }];
  const expected = items.reduce((sum, i) => sum + i.price, 0);
  expect(calculateTotal(items)).toBe(expected);
});

// GOOD: Expected value is an independent, known literal
test("calculateTotal sums line items", () => {
  expect(calculateTotal([{ price: 10 }, { price: 5 }])).toBe(15);
});
```

The same trap in other clothes: a snapshot derived by hand the same way the code
derives it, or a constant asserted equal to itself. Expected values must come from
an independent source of truth — a known-good literal, a worked example, the spec.

**Vacuous tests**: The arrangement never reaches the behaviour being claimed, so the
assertion is true for a reason that has nothing to do with the code under test. Unlike
a tautological test, the expected value is honest — the test simply never gets there.

```typescript
// BAD: clearing the session marker makes the app render sign-in directly, so the
// component that owns the handler never mounts. The handler under test never runs,
// and the assertion passes for an unrelated reason.
test("an ended session does not sign out on the server", async () => {
  localStorage.removeItem("auth_session");
  await page.reload();
  expect(await refreshStatus()).toBe(200);
});

// GOOD: a token stale but NOT expired still renders as signed in, so the component
// mounts and its connect-time renewal reaches the handler.
test("an ended session does not sign out on the server", async () => {
  setExpiry(Date.now() + 30_000);
  await page.reload();
  expect(await refreshStatus()).toBe(200);
});
```

Absence assertions are the usual home for this. "Does not call X", "makes no request",
"is not signed out" are all satisfied by a code path that does nothing at all, including
one that never ran.

## Proving a Test Guards Something

Watching a test fail before writing the implementation is necessary and not sufficient.
It catches a test that passes against *no* implementation; it says nothing about a test
that would also pass against a *wrong* one. A tautological test cannot fail. A vacuous
test fails for the wrong reason and then passes for the wrong reason.

The check that catches both: **break the implementation on purpose and confirm the test
fails.**

```
1. Get to GREEN.
2. Mutate the code the test claims to guard — invert the condition, drop the argument,
   restore the old call, remove the guard clause.
3. Re-run. The test MUST fail, and the failure message should name the behaviour.
4. Revert the mutation. Confirm GREEN again.
```

Choose the mutation that resembles the mistake a future maintainer would actually make:
re-wiring a handler to the old function, comparing by the wrong key, reusing whatever is
in storage rather than a specific value. If no such mutation makes the test fail, the
test is decoration — rewrite it or delete it.

Worth the extra minute for any test guarding a defect, any absence assertion, and any
test whose setup is elaborate enough that you cannot see at a glance which line makes it
pass.
