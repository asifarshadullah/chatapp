# Scenario N: [Feature Name]

## Requirement (as from a product manager)

[Write the feature in plain English. No technical terms. As if describing it to someone
who has never seen the code.]

---

## Questions to answer before designing

Work through these yourself before reading design.md:

1. What is the new business concept? Does it have an invariant?
2. Does this require a new entity, or modifying an existing one?
   Ask: "Does it have its own identity, lifecycle, and rules independent of existing entities?"
3. Where does the invariant live — domain method or application method?
   Ask: "Is it pure in-memory logic with no I/O?"
4. What is the orchestration workflow (the application service)?
5. What new repository methods are needed?
6. What changes in Infrastructure (new document fields, new mapping)?
7. What does the API endpoint look like?

---

## Your sketch (fill this out before reading design.md)

**Domain changes:**
-

**Application changes:**
-

**Infrastructure changes:**
-

**API changes:**
-

**Potential problem I foresee:**
-
