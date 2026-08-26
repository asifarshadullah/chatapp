# Scenario 01: Archive Conversation

## Requirement (as from a product manager)

"Users should be able to archive a conversation. Once a conversation is archived, no new
messages can be added to it. An archived conversation can still be read (history is available).
Archiving is permanent — there is no unarchive."

---

## Questions to ask before designing

Before touching code, answer these:

1. What is the new business concept? What is the invariant?
2. Does this require a new entity, or modifying an existing one?
3. Where does the invariant live (domain or application)?
4. What is the workflow (application service)?
5. What changes in infrastructure?
6. What changes in the API?

---

## Answers (work these out yourself first, then compare to design.md)

Try to sketch:
- What new properties/methods go on which domain entities
- What new service methods the Application needs
- What new repository methods Infrastructure needs
- What new endpoint the API exposes

Then read design.md to compare.
