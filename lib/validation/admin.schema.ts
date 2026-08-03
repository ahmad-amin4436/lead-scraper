import { z } from 'zod';

export const createUserSchema = z.object({
  firstName: z.string().trim().min(1, 'First name is required').max(100, 'First name is too long'),
  lastName: z.string().trim().min(1, 'Last name is required').max(100, 'Last name is too long'),
  email: z.string().trim().email('Enter a valid email address').max(256, 'Email is too long'),
  password: z
    .string()
    .min(8, 'Password must be at least 8 characters')
    .max(128, 'Password is too long'),
});

export const createRoleSchema = z.object({
  name: z.string().trim().min(1, 'Role name is required').max(128, 'Role name is too long'),
  description: z.string().trim().max(512, 'Description is too long'),
});

export type CreateUserInput = z.infer<typeof createUserSchema>;
export type CreateRoleInput = z.infer<typeof createRoleSchema>;
