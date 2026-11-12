CREATE OR REPLACE PROCEDURE get_inventory_summary(INOUT result refcursor)
LANGUAGE plpgsql AS $$ BEGIN
 OPEN result FOR SELECT c."Id" AS "CategoryId",c."Name",COUNT(p."Id")::integer AS "ProductCount"
 FROM "Categories" c LEFT JOIN "Products" p ON p."CategoryId"=c."Id" GROUP BY c."Id",c."Name" ORDER BY c."Id";
END; $$;

CREATE OR REPLACE PROCEDURE get_birthday_order_totals(INOUT result refcursor)
LANGUAGE plpgsql AS $$ BEGIN
 OPEN result FOR SELECT c."Id" AS "CustomerId",c."FirstName",c."LastName",COALESCE(SUM(o."Amount"),0)::numeric AS "Total"
 FROM "Customers" c LEFT JOIN "Orders" o ON o."CustomerId"=c."Id" AND o."Status"='Completed'
 AND EXTRACT(MONTH FROM o."OrderedAt" AT TIME ZONE 'Europe/Moscow')=EXTRACT(MONTH FROM c."BirthDate")
 AND EXTRACT(DAY FROM o."OrderedAt" AT TIME ZONE 'Europe/Moscow')=EXTRACT(DAY FROM c."BirthDate")
 GROUP BY c."Id",c."FirstName",c."LastName" ORDER BY c."Id";
END; $$;

CREATE OR REPLACE PROCEDURE get_hourly_order_averages(INOUT result refcursor)
LANGUAGE plpgsql AS $$ BEGIN
 OPEN result FOR SELECT h AS "Hour",COALESCE(ROUND(AVG(o."Amount"),2),0)::numeric AS "Average",COUNT(o."Id")::integer AS "Count"
 FROM generate_series(0,23) h LEFT JOIN "Orders" o ON o."Status"='Completed'
 AND EXTRACT(HOUR FROM o."OrderedAt" AT TIME ZONE 'Europe/Moscow')=h
 GROUP BY h ORDER BY "Average" DESC,h;
END; $$;
