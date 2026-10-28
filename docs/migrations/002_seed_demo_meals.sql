-- Sample saved meals for the VR demo ("Judge") account, so "log my usual breakfast" has something to find.
-- Applied to the A Better U database on 2026-10-28. These are demo data, not a real judge's own meals.
-- Ingredients are foods that carry real nutrition values; kcal per serving: 375, 447 and 296.
-- Uses the same tables and columns the web app writes (meals + meal_ingredients, owner_type 'individual').

with m as (
  insert into meals (name, description, created_by_user_id, owner_type, visibility, servings, is_combo, default_meal_slot)
  values
   ('Usual breakfast', 'Porridge oats, Greek yoghurt and a banana. Sample meal seeded for the VR demo account.', 'f62da52f-7cd2-4248-bb43-d0e616493b5a', 'individual', 'private', 1, false, 'breakfast'),
   ('Chicken and rice bowl', 'Diced chicken breast with basmati rice. Sample meal seeded for the VR demo account.', 'f62da52f-7cd2-4248-bb43-d0e616493b5a', 'individual', 'private', 1, false, 'dinner'),
   ('Toast and banana', 'Two slices of wholemeal toast and a banana. Sample combo seeded for the VR demo account.', 'f62da52f-7cd2-4248-bb43-d0e616493b5a', 'individual', 'private', 1, true, 'snack')
  returning id, name
)
insert into meal_ingredients (meal_id, food_id, quantity_g, sort_order)
select m.id, v.food_id::uuid, v.qty, v.ord from m join (values
  ('Usual breakfast','f7ff0181-f2d6-4049-af9c-53730dd82f24',40,0),    -- Quaker porridge oats
  ('Usual breakfast','1653295d-a40f-4596-a264-ec676de7aeb6',100,1),   -- Yeo Valley Greek-style yoghurt
  ('Usual breakfast','43590302-2cd6-4a4d-b98c-d3d7733969dd',100,2),   -- Weatherspoons banana (per-serving values; 100 g = 1 serving)
  ('Chicken and rice bowl','c56f2685-9e3f-417b-868d-a1a9e7f08db0',150,0),  -- Tesco British diced chicken breast
  ('Chicken and rice bowl','97d448d9-c2c4-4f49-9c39-894b017cc340',200,1),  -- basmati rice, easy cook, boiled
  ('Toast and banana','b2d2d1a7-3261-4a7d-a99d-e67327c36242',80,0),   -- Hovis wholemeal bread, two slices
  ('Toast and banana','43590302-2cd6-4a4d-b98c-d3d7733969dd',100,1)
) as v(meal_name, food_id, qty, ord) on v.meal_name = m.name;
