import sqlite3
import os

db_path = os.path.join(os.path.dirname(__file__), 'DeliveryControl.db')
print(f"Opening: {db_path}")

conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Check and add Remark to PullingRecords
cursor.execute("PRAGMA table_info(PullingRecords)")
cols = [row[1] for row in cursor.fetchall()]
print(f"PullingRecords columns: {cols}")
if 'Remark' not in cols:
    cursor.execute("ALTER TABLE PullingRecords ADD COLUMN Remark TEXT NOT NULL DEFAULT 'Match'")
    print("Added Remark to PullingRecords")
else:
    print("Remark already exists in PullingRecords")

# Check and add Remark to PreparationRecords
cursor.execute("PRAGMA table_info(PreparationRecords)")
cols = [row[1] for row in cursor.fetchall()]
print(f"PreparationRecords columns: {cols}")
if 'Remark' not in cols:
    cursor.execute("ALTER TABLE PreparationRecords ADD COLUMN Remark TEXT NOT NULL DEFAULT 'Match'")
    print("Added Remark to PreparationRecords")
else:
    print("Remark already exists in PreparationRecords")

conn.commit()
conn.close()
print("Done.")
