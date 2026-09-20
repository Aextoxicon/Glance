FROM node:20-alpine AS build

WORKDIR /app
COPY package.json ./
RUN npm install

FROM node:20-alpine
WORKDIR /app

COPY --from=build /app/node_modules ./node_modules
ENV NODE_ENV=production
EXPOSE 8080

CMD ["npm", "start"]
